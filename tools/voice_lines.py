# -*- coding: utf-8 -*-
"""
What to record, and what to call the file when you have.

Voice.cs names a recording after what is said -- the speaker, then a hash of the exact line --
so nothing has to be kept in a list by hand and there is no numbering to renumber. The cost of
that is you cannot guess a filename, which is what this is for. It reproduces Voice.Key exactly.

Two ways to get a list, and they answer different questions:

  --log      Read Hoodrich.log and pull out every line that WANTED audio and did not have it.
             Turn LogLevel up to Debug, play, and the game writes your to-record list for you.
             This is the reliable one: it only ever lists lines that actually reached a screen,
             with the speaker the screen actually used.

  --data     Walk the json and list what could be recorded before ever launching the game.

  --source   The whole sentences written into the C# talk files, which is most of what the
             people you stand in front of actually say.

  --texts    A courier's phone messages. OFF BY DEFAULT for the same reason the feed is: they
             go through Notify.Text onto the phone and never past it. See from_data.
  --feed     The social posts. OFF BY DEFAULT, because a post is read on a phone screen and
             nobody speaks it -- they were most of every list this ever printed and not one of
             them was ever going to be recorded.

Output is CSV: filename, speaker, text. Feed the text column to ElevenLabs and save each
result as the filename column, into scripts\\Hoodrich\\voice.

    python tools/voice_lines.py --log > to_record.csv
    python tools/voice_lines.py --data --who Gerald > gerald.csv
"""

import argparse
import csv
import glob
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


# ---------------------------------------------------------------- the naming
#
# These three must agree with Voice.cs to the character. If a key here does not match one the
# game logs, the recording is silently never found -- so they are written to mirror the C#
# line for line rather than to be short.

def tidy(line):
    """Voice.Tidy -- drop ~tag~ regions, flatten whitespace."""
    out, skipping = [], False

    for c in line:
        if c == "~":
            skipping = not skipping
            continue
        if skipping:
            continue
        out.append(" " if c in "\n\r\t" else c)

    text, collapsed, space = "".join(out), [], False

    for c in text:
        if c == " ":
            if not space:
                collapsed.append(c)
            space = True
        else:
            collapsed.append(c)
            space = False

    return "".join(collapsed).strip()


def slug(name):
    """Voice.Slug -- lowercase, runs of anything else become one underscore."""
    if not name:
        return "someone"

    out = []
    for c in name:
        if c.isalnum():
            out.append(c.lower())
        elif out and out[-1] != "_":
            out.append("_")

    return "".join(out).strip("_")


def fnv1a(text):
    """Voice.Hash -- FNV-1a, 32 bit, over the UTF-8 bytes."""
    h = 2166136261
    for b in text.encode("utf-8"):
        h = ((h ^ b) * 16777619) & 0xFFFFFFFF
    return h


def key(speaker, line):
    """Voice.Key."""
    return "%s_%08x" % (slug(speaker), fnv1a(tidy(line)))


# ------------------------------------------------------------------- the log
#
# Voice.Say writes one of these every time it comes up empty, which makes playing the game the
# most accurate way to find out what needs recording.

MISS = re.compile(r"Voice: nothing for (\S+) -- (.*)$")


def from_log(path):
    if not os.path.exists(path):
        sys.exit("no log at %s -- set LogLevel=Debug and play a bit first" % path)

    seen, rows = set(), []

    for raw in io.open(path, encoding="utf-8", errors="replace"):
        m = MISS.search(raw)
        if not m:
            continue

        name, text = m.group(1), m.group(2).rstrip()
        if name in seen:
            continue

        seen.add(name)
        rows.append((name + ".mp3", name.rsplit("_", 1)[0], text))

    return rows


# ------------------------------------------------------------------ the data
#
# Every speaker here is known without running anything, which is what makes them listable up
# front. The source-authored speeches are not, and that is what --log is for.

def from_data(root, texts=False):
    rows, seen = [], set()

    def add(speaker, text):
        if not text or len(text) < 12 or text.count(" ") < 2:
            return

        name = key(speaker, text)
        if name in seen:
            return

        seen.add(name)
        rows.append((name + ".mp3", speaker, tidy(text)))

    def load(f):
        p = os.path.join(root, "data", f)
        return json.load(io.open(p, encoding="utf-8-sig")) if os.path.exists(p) else None

    # Dealers say their own shop lines.
    doc = load("dealers.json")
    for d in (doc or {}).get("dealers", []):
        # A DEALER CAN BE TWO PEOPLE. The docks entry carries an "afterCheng" block: who is
        # stood there once the old man has been told about his son, sober and on time and a
        # different name entirely. It is loaded through the same reader as the block above and
        # every line in it is a line somebody says out loud -- and no list has ever included
        # one of them, so his whole part has been invisible to whoever was recording.
        for block in (d, d.get("afterCheng") or {}):
            who = block.get("name") or d.get("name", "")
            if not who:
                continue

            for field in ("greeting", "buyLine", "sourceReply", "sourceTooSoon", "farewell"):
                add(who, block.get(field))

            # THE FOUR HE SAYS WHEN SOMETHING IS IN THE WAY, and the four he borrows when the
            # file has not written him one.
            #
            # NONE OF THESE HAS EVER BEEN ON A LIST. DealerTalk.His takes the dealer's own line
            # if there is one and a shared default if there is not -- so every plug in the mod
            # speaks all four, and the only way any of them was ever discovered was by standing
            # in front of somebody with a full stash house and reading the log afterwards. That
            # is how "Stand aside. I'll put it inside for you." came to be silent for everybody
            # who was not Gerald: it is a literal in the C# and nothing walks the C# looking for
            # literals with a speaker attached to them.
            #
            # The shared text is repeated here rather than parsed out of DealerTalk.cs. A
            # regex over source for the second argument of a two-argument call is a guess; four
            # sentences copied across is a fact, and the assert below fails loudly the day one
            # of them is reworded and this is not.
            for field, shared in (("noRoomLine", "Come back when you got somewhere to put it."),
                                  ("noSpaceLine", "You got nowhere to put it. Sort that out first."),
                                  ("handOverLine", "Don't stand there holding it. Go on."),
                                  ("walkItInLine", "Stand aside. I'll put it inside for you.")):
                add(who, block.get(field) or shared)

            # NOBODY SPEAKS A TEXT. These five go through Notify.Text, which puts a message on
            # the phone -- and Notify never calls Voice, so no recording of one has ever been
            # played or ever could be. They sat on this list for months, sixteen of them for
            # Hao alone, reading exactly like what they are: "im here!! come out come out" is
            # not a performance.
            #
            # numberLine IS ONE OF THEM and was on the spoken list until now. "Take my number"
            # arrives as a message headed "here's my number" -- see DealerManager, which posts
            # it through Notify.Text like the rest -- so every plug in the file has been
            # carrying a to-record line that could never be heard. DealerTalk shows the same
            # string on screen at the end of a conversation, which is reading it, not saying
            # it.
            #
            # Listed on request rather than dropped, because somebody rewriting the courier's
            # messages still wants to see them.
            if texts:
                add(who, block.get("openingText"))
                add(who, block.get("numberLine"))

                for field in ("textCalled", "textLeaving", "textOutside"):
                    for line in block.get(field) or []:
                        add(who, line)

        # And the shop line, which the source builds with the money on you and the room at the
        # house stapled to the end, so it names itself rather than hashing: <slug>_shop when
        # the stock is whole, <slug>_shopcut when it has been stepped on. The recording leaves
        # the arithmetic out; the screen is already showing it. See DealerTalk.Node.
        # THE NAMED LINES. Each is a sentence the SOURCE builds with live arithmetic stapled
        # on, so no hash can ever name it -- see DealerTalk. The recording says the words and
        # the screen keeps the numbers.
        for tag, text in (("shop", "Ain't got time to stand here. What you taking? Nothing here's been touched."),
                          ("shopcut", "Ain't got time to stand here. What you taking? It's all stepped on already."),
                          ("amount", "How many? And don't say one if you mean four.")):
            # A DEALER WHO NEVER CUTS ANYTHING NEVER SAYS THE CUT LINE. DealerTalk picks
            # between these two on PurityNow, and a dealer pinned at 1.0 can only ever come
            # out of that comparison one way -- so the other file is one the game cannot ask
            # for. Hao is that dealer: uncut is the whole of his pitch.
            if tag == "shopcut":
                try:
                    if float(d.get("purity", 0) or 0) >= 0.999:
                        continue
                except (TypeError, ValueError):
                    pass

            name = slug(d.get("name", "")) + "_" + tag
            if name not in seen:
                seen.add(name)
                rows.append((name + ".mp3", d.get("name", ""), text))

    # Lamar owns MOST of the mission list, and "most" is the whole of the trouble.
    #
    # "Lamar", NOT "Lamar Davis". The key is built from whatever string the screen was handed
    # as the speaker, and that comes from Fixer.Name, which is the bare first name. This said
    # Lamar Davis for a long time, so every mission row it printed was named lamar_davis_...
    # and not one of them was ever a file the game asked for. A recording made from this list
    # would have sat in the folder in silence.
    #
    # WHICH IS EXACTLY WHAT THEN HAPPENED TO VERNON. A mission can name a `giver`, and the one
    # that does is briefed by a different man in a different room -- his own basement -- with
    # his own name on the panel. The panel's name is the speaker, so the game asks for
    # vernon_<hash>.wav; this list said Lamar, so the takes were cut, named lamar_<hash>.wav,
    # filed, and never once played. Nothing errors. The line simply does not speak, and it does
    # not speak for as long as nobody stands in that basement and notices.
    #
    # Same hash either way -- the hash is of the sentence and the speaker is only the prefix --
    # so a wrongly named take is a rename, not a re-record.
    doc = load("missions.json")
    for m in (doc or {}).get("missions", []):
        who = Giver(m)

        for field in ("brief", "done", "briefAgain", "doneAgain"):
            add(who, m.get(field))
        for line in m.get("briefMore") or []:
            add(who, line)

    return rows


def Giver(mission):
    """Who says this job's lines: the mission's own giver, or Lamar when it has none.

    THE NAME AS THE PANEL PRINTS IT, because the panel's name is what the key is built from.
    Anything not in here falls back to the id with a capital on it, which is right for a
    one-word first name and is the shape every giver has had so far.
    """
    who = (mission.get("giver") or "").strip()

    if not who:
        return "Lamar"

    return {"vernon": "Vernon", "gerald": "Gerald", "hao": "Hao", "lamar": "Lamar"}.get(
        who.lower(), who[:1].upper() + who[1:])


# ------------------------------------------------------------------- the feed
#
# NOBODY SPEAKS A POST. The feed is text on a phone screen and it is read, not heard, so its
# thousands of lines are not work waiting to be done -- they were the bulk of every list this
# tool has ever printed and none of it was ever going to be recorded.
#
# It is still listable, because the day somebody wants a voice reading the timeline out it is
# one flag away. Off by default is the only part that changed.
#
# Half of them could not be recorded anyway: a post is written with {hood}, {street} and the
# rest, filled in from wherever the player is standing when it goes up, and a key is a hash of
# the FINISHED sentence -- so the same post names a different file in every neighbourhood.
# That is the feature working, not a fault to fix.

def from_feed(root):
    rows, seen = [], set()

    p = os.path.join(root, "data", "socials.json")
    doc = json.load(io.open(p, encoding="utf-8-sig")) if os.path.exists(p) else {}

    who = {a.get("voice"): a.get("name") for a in doc.get("authors", []) if a.get("voice")}

    for voice, cats in (doc.get("voices") or {}).items():
        speaker = who.get(voice, voice)

        for lines in (cats or {}).values():
            for line in lines or []:
                if not line or len(line) < 12 or line.count(" ") < 2:
                    continue

                name = key(speaker, line)
                if name in seen:
                    continue

                seen.add(name)
                rows.append((name + ".mp3", speaker, tidy(line)))

    return rows


# --------------------------------------------------------------- the source
#
# A conversation written in C# is still a fixed sentence -- FixerTalk hands Node() a literal
# and the screen says it word for word. Those are as recordable as anything in the json and
# --data used to miss every one of them.
#
# ONLY THE ONES THAT ARE WHOLE. "Gimme like " + minutes + " minutes" is three pieces and only
# one of them is written down; the game keys those by tag instead (see Voiced/Voice.Named), so
# a half-sentence lifted out of one would name a file nothing ever asks for.

# WHICH NAME EACH FILE BUILDS ITS NODES WITH.
#
# Every one of these has a Node() helper that stamps the same speaker on everything it makes,
# so the file IS the speaker -- except DealerTalk, where the node is built with whichever
# dealer you walked up to, and both of them can reach most of it. Two names there, and a line
# from that file wants a recording under each.
#
# PortRun writes its speaker out in the call, so it is listed with none and answers for
# itself below.

# A FILE MAY BUILD ITS LINES UNDER MORE THAN ONE NAME.
#
# VernonTalk is the first: Node() stamps "Vernon" on everything he says and Bar()/Land() stamp
# "OG Vee" on everything he raps, because the two are recorded in different voices by somebody
# who cannot cut audio -- so the file name has to be the thing that says which voice a take
# wants. A third element on a row is that map: helper name to the speaker it stamps.

SPEECH = [
    (r"src\Hoodrich\Gangs\LeaderTalk.cs",       ["Gerald"]),
    (r"src\Hoodrich\Locations\ArmourerTalk.cs", ["Stretch"]),
    (r"src\Hoodrich\Locations\HaoTalk.cs",      ["Hao"]),
    (r"src\Hoodrich\Locations\VernonTalk.cs",   ["Vernon"], {"Verse": "OG Vee"}),

    # The man rather than the conversation. Nothing in here builds a dialogue node -- it is
    # the ped, the wall and the walk to his car -- but he speaks on the way out of the shop,
    # and a line said outside a conversation is still a line somebody has to record.
    (r"src\Hoodrich\Locations\Vernon.cs",       ["Vernon"]),
    # Vernon's job, and every word of it was invisible: this file was not listed at all, so
    # the barks he shouts across that yard -- the panic, the grab, the drive out -- had never
    # once appeared on a list of things to record.
    (r"src\Hoodrich\Missions\Deal.cs",          ["Vernon"], {"Vees": "Vernon"}),
    (r"src\Hoodrich\Missions\FixerTalk.cs",     ["Lamar"]),
    (r"src\Hoodrich\Missions\BikeRide.cs",      ["Lamar"]),
    (r"src\Hoodrich\Missions\Hunt.cs",          ["Lamar"]),
    (r"src\Hoodrich\Missions\TagRun.cs",        ["Lamar"]),
    (r"src\Hoodrich\Missions\PortRun.cs",       []),
    (r"src\Hoodrich\Supply\DealerTalk.cs",      ["Tao Cheng", "Gerald"]),
    (r"src\Hoodrich\Supply\Stoop.cs",           []),
]

CALL = re.compile(r'\b(?:Node|Nothing|Step)\(\s*(?:[A-Za-z_][\w.]*\s*,\s*){0,2}((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)', re.S)
ASSIGN = re.compile(r'\bline\s*=\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+);', re.S)
EXPLICIT = re.compile(r'new DialogueNode\(\s*"([^"]+)"\s*,\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)', re.S)

# A LINE HELD IN A CONSTANT AND SPOKEN BY NAME.
#
# Every other pattern here finds a literal sitting where a sentence goes. This finds the case
# where the sentence was given a name first -- Voice.Say("vernon", RoundTheSide) -- which is
# what you do the moment a line is needed twice, once for the audio and once for the subtitle.
# It looks exactly like a call with no literal in it, so it was extracted by nothing, had no
# filename, was on no list to record, and simply never spoke.
#
# ONLY CONSTANTS THAT ARE ACTUALLY SAID: the name has to turn up in a Voice.Say in the same
# file. A const string on its own is as likely to be an anim dictionary or a stage name.
SAID = re.compile(r'\bVoice\.Say\(\s*"([^"]+)"\s*,\s*([A-Za-z_]\w*)\s*[,)]')


def CONST(name):
    return re.compile(r'\bconst\s+string\s+' + re.escape(name) +
                      r'\s*=\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+);', re.S)
PIECE = re.compile(r'"((?:[^"\\]|\\.)*)"')

# Step is Vernon's tour of his own basement -- one stop per node, built through a helper
# rather than through Node directly, which made every line of it invisible to this file
# while sitting in plain sight in VernonTalk. A helper that wraps Node is still dialogue.
# LINES NOBODY READS OFF A SCREEN.
#
# Everything above finds dialogue attached to a conversation NODE, because for most of
# this mod that is what dialogue is. The hunt is the exception: Lamar talks to you while
# you are crouched behind a fence, with no panel open and no text anywhere -- see
# Hunt.Word -- and those lines were invisible to both halves of this tool. They never
# appeared in a to-record list, and when they WERE recorded anyway, take_voice.py could
# not place the files because it did not know the words existed.
#
# WORD is the call itself. NAMED covers the arrays of interchangeable lines the same file
# picks from at random, listed by name rather than matched by shape, because "a string
# array in a C# file" is also every model name and every scenario in the mod. TERNARY
# catches the one shape that assigns a spoken line through a conditional before handing
# it over -- "var line = down >= need ? this : that" -- which ASSIGN cannot see.
WORD = re.compile(r'\bWord\(\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)', re.S)
# A run of interchangeable lines, by the names those arrays get given. The *Words suffix is
# the convention for "a spoken line with words in it" as opposed to the game's own ambient
# speech lists sitting beside them -- see BikeRide, where RideLines is CHAT_STATE and OutWords
# is a sentence. Only files listed in SPEECH are read at all, so the runner's own caption
# arrays are not swept in by this.
NAMED = re.compile(r'\bstring\[\]\s+(?:\w*Words|Nudges|Panic)\s*=\s*\{(.*?)\};', re.S)
TERNARY = re.compile(r'\bvar\s+line\s*=\s*([^;]*?"[^;]*?);', re.S)


def from_source(root):
    rows, seen = [], set()

    def add(speaker, line):
        if not speaker or len(line) < 12 or line.count(" ") < 2:
            return

        name = key(speaker, line)
        if name in seen:
            return

        seen.add(name)
        rows.append((name + ".mp3", speaker, tidy(line)))

    def elements(blob):
        """One entry per array element, with its own wrapped pieces still joined up.

        THIS IS WHY THE LIST HAD HALF SENTENCES IN IT. An array of alternatives was read a
        LITERAL at a time, which is right for a bag of short barks written one per line and
        wrong the moment one of them is too long for a line and gets wrapped with a +. The
        record list then carries "on you --" and "room at the house" as though they were
        things somebody says, and the real line -- the whole sentence -- is on it nowhere.

        So the split is on the COMMAS BETWEEN ELEMENTS, which means knowing which commas are
        inside a string. Everything between two of them is one element however it is laid
        out, and joined puts its pieces back together.
        """
        out, at, depth, start = [], 0, 0, 0
        instr = False

        while at < len(blob):
            c = blob[at]

            if instr:
                if c == "\\":
                    at += 2
                    continue
                if c == '"':
                    instr = False
            elif c == '"':
                instr = True
            elif c in "([{":
                depth += 1
            elif c in ")]}":
                depth -= 1
            elif c == "," and depth == 0:
                out.append(blob[start:at])
                start = at + 1

            at += 1

        out.append(blob[start:])

        return [e for e in out if '"' in e]

    def joined(blob):
        # \n IS A LINE BREAK, NOT TWO CHARACTERS. Voice.Tidy flattens whitespace before it
        # hashes, so a paragraph break in a speech becomes one space -- and a key built from
        # a literal backslash-n instead is a key the game will never ask for. Every long
        # speech in this mod has paragraph breaks in it.
        out = []
        for p in PIECE.findall(blob):
            out.append(p.replace('\\"', '"')
                        .replace("\\n", "\n").replace("\\r", "\r").replace("\\t", "\t")
                        .replace("\\\\", "\\"))
        return "".join(out)

    for row in SPEECH:
        rel, speakers = row[0], row[1]
        helpers = row[2] if len(row) > 2 else {}

        path = os.path.join(root, rel)
        if not os.path.exists(path):
            continue

        text = io.open(path, encoding="utf-8-sig").read()

        # Comment lines are prose about the code, and there is a great deal of it.
        body = "\n".join(l for l in text.split("\n")
                         if not l.strip().startswith("//"))

        def whole(m, group):
            """False when the literal runs on into a variable and is only half a sentence."""
            if m.group(group).rstrip().endswith("+"):
                return False
            return not body[m.end():].lstrip().startswith("+")

        # A node that names its own speaker answers for itself, whatever the file is.
        for m in EXPLICIT.finditer(body):
            if whole(m, 2):
                add(m.group(1), joined(m.group(2)))

        for rx in (CALL, ASSIGN, WORD):
            for m in rx.finditer(body):
                if not whole(m, 1):
                    continue

                line = joined(m.group(1))

                for who in speakers:
                    add(who, line)

        # A run of interchangeable lines, and a line chosen by a conditional. Each literal in
        # the region stands on its own rather than being joined to its neighbours -- these are
        # alternatives, not one sentence split across lines. See NAMED and TERNARY.
        # An array: comma between elements, and an element may be wrapped over three lines.
        for m in NAMED.finditer(body):
            for element in elements(m.group(1)):
                for who in speakers:
                    add(who, joined(element))

        # A conditional: the alternatives have a COLON between them and no comma at all, so
        # elements() would hand back both halves as one sentence. Each literal stands alone
        # here, which is what it was doing before and is right for this shape.
        for m in TERNARY.finditer(body):
            for piece in PIECE.findall(m.group(1)):
                for who in speakers:
                    add(who, joined('"' + piece + '"'))

        # A sentence that was given a name before it was said. See SAID.
        for m in SAID.finditer(body):
            who, named = m.group(1), m.group(2)

            held = CONST(named).search(body)
            if not held:
                continue

            # The call spells the speaker the way the KEY wants it; the list wants it the way a
            # person reads it, which is how the file is labelled already.
            shown = next((w for w in speakers if w.lower() == who.lower()), who.capitalize())

            add(shown, joined(held.group(1)))

        # And the helpers that stamp a different name. See SPEECH.
        for helper, who in helpers.items():
            rx = re.compile(r"\b" + helper + r'\(\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)', re.S)

            for m in rx.finditer(body):
                if whole(m, 1):
                    add(who, joined(m.group(1)))

            # AND THE SAME HELPER HANDED A NAME INSTEAD OF THE WORDS. Vees(RollOut) is the
            # same act as Vees("..."), and a mission keeps its barks in consts because they
            # are said from more than one beat. Only a name that resolves to a const string
            # in this file counts -- see CONST -- so a variable or a parameter is passed
            # over rather than guessed at.
            named = re.compile(r"\b" + helper + r"\(\s*([A-Za-z_]\w*)\s*[,)]")

            for m in named.finditer(body):
                held = CONST(m.group(1)).search(body)
                if held:
                    add(who, joined(held.group(1)))

    return rows


# ------------------------------------------------------------------ the have
#
# Which of these are already sat in the voice folder. The answer is what somebody actually
# wants from this: not "here is everything", but "here is what is left".

def recorded(folder):
    have = set()

    if folder and os.path.isdir(folder):
        for f in os.listdir(folder):
            stem, ext = os.path.splitext(f)
            if ext.lower() in (".wav", ".mp3"):
                have.add(stem.lower())

    return have


def find_voice(root):
    tries = glob.glob(r"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V*"
                      r"\scripts\Hoodrich\voice")
    tries += [os.path.join(os.path.expanduser("~"), "Documents", "Hoodrich", "voice")]

    for p in tries:
        if os.path.isdir(p):
            return p

    return ""


# ----------------------------------------------------------------------- cli

def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--log", nargs="?", const="", metavar="PATH",
                    help="read misses out of Hoodrich.log")
    ap.add_argument("--data", action="store_true", help="list what the json holds")
    ap.add_argument("--source", action="store_true",
                    help="list the whole sentences written into the C#")
    ap.add_argument("--texts", action="store_true",
                    help="include a courier's phone messages, which are read rather than heard")
    ap.add_argument("--feed", action="store_true",
                    help="include the social posts, which are read rather than heard")
    ap.add_argument("--who", metavar="NAME", help="only this speaker")
    ap.add_argument("--need", nargs="?", const="", metavar="DIR",
                    help="only the ones not in the voice folder already")
    ap.add_argument("--root", default=HERE)
    args = ap.parse_args()

    if args.log is None and not args.data and not args.source and not args.feed:
        ap.error("pick --log, --data, --source or --feed")

    rows = []

    if args.log is not None:
        path = args.log or find_log(args.root)
        rows += from_log(path)

    if args.data:
        rows += from_data(args.root, args.texts)

    if args.source:
        rows += from_source(args.root)

    if args.feed:
        rows += from_feed(args.root)

    # The same line can come out of two of those at once -- a mission brief is in the json and
    # in the log the moment it has been on screen once.
    rows = list({r[0]: r for r in rows}.values())

    if args.who:
        want = args.who.lower()
        rows = [r for r in rows if want in r[1].lower()]

    if args.need is not None:
        have = recorded(args.need or find_voice(args.root))

        # A LINE TWO PEOPLE CAN SAY IS ONE LINE. DealerTalk builds its nodes with whichever
        # dealer you walked up to, so every sentence in it comes out under both names -- but
        # most of them sit behind a branch only one of the two can reach, and there is no way
        # to tell which from the text.
        #
        # The recordings already know. If one of the names has a take of a line, that is who
        # says it, and the other name is not missing anything. What is left over is the lines
        # nobody has recorded under any name, which are the ones actually worth looking at.
        byhash = {}
        for r in rows:
            byhash.setdefault(os.path.splitext(r[0])[0].rsplit("_", 1)[-1], []).append(r)

        keep = []
        for r in rows:
            me = os.path.splitext(r[0])[0].lower()
            if me in have:
                continue

            kin = byhash[me.rsplit("_", 1)[-1]]
            if any(os.path.splitext(o[0])[0].lower() in have for o in kin if o is not r):
                continue

            keep.append(r)

        rows = keep

    rows.sort(key=lambda r: (r[1].lower(), r[2]))

    out = csv.writer(sys.stdout, lineterminator="\n")
    out.writerow(("filename", "speaker", "text"))
    out.writerows(rows)

    sys.stderr.write("  %d line(s)\n" % len(rows))


def find_log(root):
    """The log follows Paths.Writable, so look where it actually lands."""
    tries = glob.glob(r"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V*"
                      r"\scripts\Hoodrich.log")
    tries += [os.path.join(os.path.expanduser("~"), "Documents", "Hoodrich", "Hoodrich.log"),
              os.path.join(root, "Hoodrich.log")]

    for p in tries:
        if os.path.exists(p):
            return p

    return tries[-1]


if __name__ == "__main__":
    main()
