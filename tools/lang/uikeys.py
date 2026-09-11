# -*- coding: utf-8 -*-
"""
Every string a PLAYER can read, pulled from the source so a translation can be complete.
Writes uikeys.json next to this file: {"English string": "where it came from"}.

What is collected, and from where:

  settings   every Head/Tick/Slide/Pick/Bind/Readout row in UI/SettingsScreen.cs: the label,
             the note, and the choices;
  wheel      Title = "...", Label = "...", Note = "...", Hint = "..." assignments anywhere, and
             every InfoSection .Row("label", "value", colour, "description") -- the guide
             pages and the phone's info panels;
  notices    Notify.*("...") and Help.ShowThisFrame("...") literals, including the leading
             fragment of a glued notice ("Paid " + money + ...), which is a key in its own
             right with its trailing space kept;
  labels     Draw.Text / Draw.TextRight / Hud.Text* / UiKit.Key literal first arguments --
             the footers, captions and headings;
  values     the ON/OFF words and the enum names the choice rows show.

Log.* is not collected: the log stays English by design. Dialogue is not collected: it is
data, it is voiced in English, and a translation would want a translator, not a dictionary.
"""
import collections
import glob
import io
import json
import os
import re

ROOT = r'C:\projects\hoodrich\src\Hoodrich'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'uikeys.json')

keys = collections.OrderedDict()


ESC = re.compile(r'\\(u[0-9A-Fa-f]{4}|.)')
SIMPLE = {'n': '\n', 't': '\t', '"': '"', '\\': '\\', "'": "'", '0': '\0'}


def unescape(s):
    """A C# literal's escapes, the ones this codebase uses: backslash, quote, n, t and uXXXX."""
    def one(m):
        e = m.group(1)
        if len(e) == 5 and e[0] == 'u':
            return chr(int(e[1:], 16))
        return SIMPLE.get(e, e)
    return ESC.sub(one, s)


def add(s, where):
    s = unescape(s)
    if s and s.strip() and s not in keys:
        keys[s] = where


def call_args(text, start):
    """text[start] is '(' -- the argument text up to its matching ')'."""
    depth, i, in_str = 0, start, False
    while i < len(text):
        c = text[i]
        if in_str:
            if c == '\\':
                i += 1
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c == '(':
            depth += 1
        elif c == ')':
            depth -= 1
            if depth == 0:
                return text[start + 1:i]
        i += 1
    return text[start + 1:]


def literals(args):
    """(depth, string, glued) for every literal; adjacent literals joined by + are one string,
    and a literal followed by + something-that-is-not-a-literal is marked glued."""
    out, depth, i, in_str, cur = [], 0, 0, False, None
    while i < len(args):
        c = args[i]
        if in_str:
            if c == '\\':
                cur.append(c)
                i += 1
                cur.append(args[i])
            elif c == '"':
                in_str = False
                out.append([depth, ''.join(cur), i])
            else:
                cur.append(c)
        elif c == '"':
            in_str = True
            cur = []
        elif c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        i += 1
    # join "a" + "b"; mark "a" + x
    joined = []
    for d, s, end in out:
        tail = args[end + 1:]
        m = re.match(r'\s*\+\s*("?)', tail)
        glued = bool(m) and m.group(1) != '"'
        if joined and joined[-1][3] and joined[-1][0] == d:
            joined[-1] = [d, joined[-1][1] + s, glued, bool(m) and m.group(1) == '"']
            continue
        joined.append([d, s, glued, bool(m) and m.group(1) == '"'])
    return [(d, s, glued) for d, s, glued, _ in joined]


NOT_TEXT = re.compile(r'^(?:[0-9.#,]+|~[a-z_]+~|[a-z_]+\.(?:png|json|ini|wav|xml)|\W{1,3}|\s*|[A-Z0-9_]+_[A-Z0-9_]+|STRING|[a-z]+|[A-Za-z]+_[A-Za-z_]+)$')


def keep(s):
    if NOT_TEXT.match(s):
        return False
    if len(s.strip()) < 2:
        return False
    if '�' in s:
        return False
    return True


# ---- settings -------------------------------------------------------------------------------------
ss = io.open(os.path.join(ROOT, 'UI', 'SettingsScreen.cs'), encoding='utf-8-sig').read()
body = ss[ss.index('private void Build()'):]
for m in re.finditer(r'\b(Head|Tick|Slide|Pick|Bind|Readout)\(', body):
    args = call_args(body, m.end() - 1)
    lits = literals(args)
    top = [s for d, s, g in lits if d == 0]
    kind = m.group(1)
    if not top:
        continue
    add(top[0], 'settings.label')
    rest = top[1:]
    if kind in ('Tick', 'Slide', 'Pick', 'Bind'):
        rest = rest[2:]          # section, key
    for s in rest:
        if keep(s) and not re.match(r'^[0#.]+$', s) and len(s) > 3:
            add(s, 'settings.note')
    if kind == 'Pick':
        for d, s, g in lits:
            if d == 1 and keep(s):
                add(s, 'settings.choice')
add('ON', 'settings.value')
add('OFF', 'settings.value')

# ---- wheel, guide, panels, notices, labels ------------------------------------------------------
SINK = re.compile(r'\b(?:Notify\.(?:Ticker|Important|Problem|Failure|Text|Card)|Help\.ShowThisFrame|'
                  r'Draw\.Text|Draw\.TextRight|Hud\.Text|Hud\.TextRight|Hud\.Label|UiKit\.Key|UiKit\.KeyRight)\s*\(')
for path in glob.glob(os.path.join(ROOT, '**', '*.cs'), recursive=True):
    base = os.path.basename(path)
    if base in ('Log.cs', 'SettingsScreen.cs', 'Lang.cs'):
        continue
    text = io.open(path, encoding='utf-8-sig').read()
    tag = base[:-3]
    # Title = "..." + "..." -- the whole run of glued literals is one string, as it is at runtime.
    for m in re.finditer(r'\b(Title|Label|Note|Hint|Caption|Subject)\s*=\s*("(?:[^"\\]|\\.)*"(?:\s*\+\s*"(?:[^"\\]|\\.)*")*)', text):
        whole = ''.join(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2)))
        if keep(whole):
            add(whole, tag + '.' + m.group(1).lower())
    for m in re.finditer(r'\.Row\(', text):
        lits = literals(call_args(text, m.end() - 1))
        for d, s, g in lits:
            if d == 0 and keep(s) and len(s) > 2:
                add(s, tag + '.row')
    for m in SINK.finditer(text):
        before = text[max(0, m.start() - 60):m.start()].split('\n')[-1]
        if re.search(r'(?:private|public|internal|static)\s', before):
            continue
        lits = literals(call_args(text, m.end() - 1))
        name = m.group(0).split('(')[0].strip()
        if name.startswith('Notify.Text') or name.startswith('Notify.Card'):
            tops = [(s, g) for d, s, g in lits if d == 0]
            for s, g in tops[2:]:
                if keep(s) and len(s) > 2:
                    add(s, tag + '.notice')
            continue
        for d, s, g in lits:
            if d != 0:
                continue
            if name.startswith('UiKit.Key'):
                if keep(s) and s.isupper():
                    add(s, tag + '.key')
                continue
            if keep(s) and len(s) > 2:
                add(s, tag + '.' + ('glue' if g else name.split('.')[-1].lower()))
            break   # only the first top-level literal of a Text/Notice call is the text

# ---- not text -----------------------------------------------------------------------------------------
keys = collections.OrderedDict((k, w) for k, w in keys.items() if keep(k))

io.open(OUT, 'w', encoding='utf-8', newline='\n').write(json.dumps(keys, ensure_ascii=False, indent=1) + '\n')
by = collections.Counter(w.split('.')[-1] for w in keys.values())
print("%d player-visible strings" % len(keys))
for k, n in sorted(by.items(), key=lambda x: -x[1]):
    print("   %-12s %d" % (k, n))
