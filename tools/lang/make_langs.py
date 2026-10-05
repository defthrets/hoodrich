# -*- coding: utf-8 -*-
"""
Build every data/lang/*.json from the tr_*.py dictionaries here, and refuse anything that
would misbehave in the game:

  * a colour code (~y~, ~s~, ~g~...) or a control token (~INPUT_CONTEXT~) present in the
    English and missing from the translation, or added -- the game would draw a literal
    tilde or lose a colour;
  * leading or trailing whitespace that differs -- the mod glues fragments together on
    exactly those spaces, and Lang decides what is glue by them;
  * a key written twice in the same file -- a Python dict keeps the second, silently;
  * a key that is not in uikeys.json -- reported, because it is probably a typo of one
    that is, and a typo translates nothing.

Coverage against uikeys.json (which uikeys.py rebuilds from the source) is printed per
file so a partial translation is a decision and not an accident.

It also writes each file's TAILS: the glue keys that turn up inside a line of dialogue or
the feed as well as inside a notice. Lang swaps a tail only where some other glue matched
too -- inside a notice the mod built, never inside a line somebody says. See spoken().

    python make_langs.py          from this folder
"""
import collections
import glob
import importlib.util
import io
import json
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

SP = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(SP, '..', '..', 'data', 'lang'))
sys.path.insert(0, SP)

known = json.load(io.open(os.path.join(SP, 'uikeys.json'), encoding='utf-8'))
required = [k for k in known if not re.match(r'^[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]*)+$', k)]

CODES = re.compile(r'~[A-Za-z_]+~')

FILES = [
    ('tr_enus', 'en-US'), ('tr_pt', 'pt-BR'), ('tr_es', 'es'), ('tr_fr', 'fr'), ('tr_de', 'de'),
    ('tr_ru', 'ru'), ('tr_pl', 'pl'), ('tr_zh', 'zh-CN'), ('tr_hi', 'hi'),
]


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SP, name + '.py'))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def duplicates(name):
    text = io.open(os.path.join(SP, name + '.py'), encoding='utf-8').read()
    text = re.sub(r'(?m)^\s*#.*$', '', text)
    counts = collections.Counter(re.findall(r'"((?:[^"\\]|\\.)*)"\s*:\s*"', text))
    return [k for k, n in counts.items() if n > 1]


def edge(s):
    return len(s) - len(s.lstrip()), len(s) - len(s.rstrip())


def check(table):
    bad = []
    for e, t in table.items():
        if sorted(CODES.findall(e)) != sorted(CODES.findall(t)):
            bad.append(('codes', e, t))
        # The edges must match -- except that a translation may ADD a leading space to a
        # fragment the English hangs straight off a name ("'s hurt bad."), because the
        # language needs the word gap the English apostrophe did without.
        ee, et = edge(e), edge(t)
        if ee != et and not (ee == (0, 0) and et[1] == 0 and not e[0].isalnum()):
            bad.append(('space', e, t))
        if not t.strip():
            bad.append(('empty', e, t))
    return bad


# ---- tails ------------------------------------------------------------------------------------------
#
# Lang swaps glue inside any string it is handed, and it is handed the dialogue and the feed as well
# as the notices. A glue key that also turns up inside a line somebody SAYS -- " now.", " a week.",
# " on the " -- would turn that line into two languages. Leaving those keys English instead left
# every notice built from them in two languages: "c'est l'embrouille avec Ballas now." So each glue
# key is tried against every line the mod says, and one that fires there is written out as a tail,
# which Lang only swaps inside a string where a glue key that is NOT a tail matched as well.
DATA = os.path.normpath(os.path.join(SP, '..', '..', 'data'))
SRC = os.path.normpath(os.path.join(SP, '..', '..', 'src', 'Hoodrich'))


def is_glue(key):
    """Lang.IsGlue, to the letter."""
    first, last = key[0], key[-1]
    if last in ' :$~' or key.startswith('~s~') or key.startswith("'s "):
        return True
    return not first.isalnum() and first not in '~$("\''


def spoken():
    """Every line the mod says rather than shows: every string in the data files, and the long
    sentences in the code that are not UI -- uikeys.json has the UI ones."""
    texts = []

    def walk(node):
        if isinstance(node, str):
            if len(node) > 6 and ' ' in node:
                texts.append(node)
        elif isinstance(node, list):
            for x in node:
                walk(x)
        elif isinstance(node, dict):
            for k, v in node.items():
                if not str(k).startswith('_'):          # a comment in the file, never drawn
                    walk(v)

    for path in glob.glob(os.path.join(DATA, '**', '*.json'), recursive=True):
        if os.sep + 'lang' + os.sep in path:
            continue
        try:
            walk(json.load(io.open(path, encoding='utf-8-sig')))
        except Exception:
            pass
    for path in glob.glob(os.path.join(SRC, '**', '*.cs'), recursive=True):
        text = io.open(path, encoding='utf-8-sig', errors='replace').read()
        for m in re.finditer(r'"((?:[^"\\\n]|\\.)*)"', text):
            s = m.group(1).replace('\\"', '"')
            if len(s) > 25 and s.count(' ') >= 4 and s not in known and not s.startswith(('~', 'Press ')):
                texts.append(s)
    return texts


def fires(text, key):
    """Lang.Resolve's rule: on a word boundary, and not running on into a letter."""
    i = text.find(key)
    while i >= 0:
        at = i == 0 or not text[i - 1].isalnum() or not text[i].isalnum()
        end = i + len(key)
        runs_on = end < len(text) and text[end].isalnum() and key[-1].isalnum()
        if at and not runs_on:
            return True
        i = text.find(key, i + 1)
    return False


SPOKEN = spoken()
_heard = {}


def tails_of(table):
    """The glue keys in this table that fire inside a line the mod says."""
    out = []
    for k in table:
        if len(k) < 3 or not is_glue(k):
            continue
        if k not in _heard:
            _heard[k] = [t for t in SPOKEN if fires(t, k)]
        if any(t not in table for t in _heard[k]):     # a key itself is matched whole, never scanned
            out.append(k)
    return sorted(out)


def write(code, language, by, table, note=None, tails=None):
    doc = collections.OrderedDict([('language', language), ('code', code), ('by', by)])
    doc['note'] = note or ('English on the left, ' + language + ' on the right. Keep the ~y~ colour codes, the '
                           '~INPUT_~ button names and the leading and trailing spaces exactly as they are: the mod '
                           'glues fragments together on them. Anything not listed here shows in English.')
    doc['strings'] = collections.OrderedDict(table)
    doc['tails'] = tails or []
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    path = os.path.join(OUT, code + '.json')
    with io.open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write('\n')
    json.load(io.open(path, encoding='utf-8'))
    return path


fatal = False
print("%-6s %8s %9s %8s %6s  %s" % ('file', 'strings', 'required', 'missing', 'tails', ''))

for name, code in FILES:
    if not os.path.exists(os.path.join(SP, name + '.py')):
        print("%-6s  (no %s.py yet)" % (code, name))
        continue
    m = load(name)
    table = collections.OrderedDict((k, v) for k, v in m.T.items() if k != v)
    d = duplicates(name)
    bad = check(table)
    unknown = [k for k in table if k not in known]
    missing = [k for k in required if k not in m.T]
    if d or bad:
        fatal = True
        for k in d:
            print("  DUPLICATE in %s: %r" % (name, k))
        for why, e, t in bad:
            print("  BAD %-5s in %s: %r -> %r" % (why, name, e, t))
    for k in unknown:
        print("  not in the source (typo?) in %s: %r" % (name, k[:70]))
    note = None
    if code == 'zh-CN':
        note = ('English on the left, Chinese on the right. Keep the ~y~ colour codes, the ~INPUT_~ button names '
                'and the leading and trailing spaces as they are. GTA V only has CJK glyphs when the game itself '
                'is set to Chinese; in any other game language these strings draw as nothing. Anything not listed '
                'shows in English.')
    if code == 'hi':
        note = ('English on the left, Hindi in Latin letters on the right -- the way it is typed, because the '
                "game's fonts have no Devanagari in any language. Keep the ~y~ colour codes, the ~INPUT_~ button "
                'names and the leading and trailing spaces exactly as they are. Anything not listed shows in English.')
    if code == 'en-US':
        missing = []      # a spelling overlay, deliberately sparse
    tails = tails_of(table)
    write(code, m.LANGUAGE, m.BY, table, note, tails)
    print("%-6s %8d %9d %8d %6d  %s" % (code, len(table), len(required), len(missing), len(tails),
                                        ('; '.join(repr(x)[:36] for x in missing[:3]) + (' ...' if len(missing) > 3 else '')) if missing else ''))

if fatal:
    print("\nFAILED")
    sys.exit(1)
print("\nall language files written and re-parsed as strict JSON")
