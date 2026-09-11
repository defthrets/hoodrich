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

    python make_langs.py          from this folder
"""
import collections
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


def write(code, language, by, table, note=None):
    doc = collections.OrderedDict([('language', language), ('code', code), ('by', by)])
    doc['note'] = note or ('English on the left, ' + language + ' on the right. Keep the ~y~ colour codes, the '
                           '~INPUT_~ button names and the leading and trailing spaces exactly as they are: the mod '
                           'glues fragments together on them. Anything not listed here shows in English.')
    doc['strings'] = collections.OrderedDict(table)
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    path = os.path.join(OUT, code + '.json')
    with io.open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write('\n')
    json.load(io.open(path, encoding='utf-8'))
    return path


fatal = False
print("%-6s %8s %9s %8s  %s" % ('file', 'strings', 'required', 'missing', ''))

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
    write(code, m.LANGUAGE, m.BY, table, note)
    print("%-6s %8d %9d %8d  %s" % (code, len(table), len(required), len(missing),
                                    ('; '.join(repr(x)[:36] for x in missing[:3]) + (' ...' if len(missing) > 3 else '')) if missing else ''))

if fatal:
    print("\nFAILED")
    sys.exit(1)
print("\nall language files written and re-parsed as strict JSON")
