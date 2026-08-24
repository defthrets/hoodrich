using System;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// What Hao says.
    ///
    /// He is the only person in this mod who does not care about product, and the script has
    /// to carry that on its own -- so he never mentions weight, never asks what you are
    /// holding, and measures everybody by what they turn up in. Cars are the subject, cars are
    /// the currency, and the reason he is worth knowing is that he can make one stop being
    /// what it was.
    ///
    /// The re-vin work is set up here and deliberately not offered yet. He explains what he
    /// does, says he is not handing it to somebody he met ten seconds ago, and leaves it
    /// standing -- so when the jobs land they are the thing he already told you about rather
    /// than a menu item that appeared.
    /// </summary>
    internal sealed class HaoTalk
    {
        private readonly Hao _hao;
        private readonly PlayerState _state;

        public HaoTalk(Hao hao, PlayerState state)
        {
            _hao = hao;
            _state = state;
        }

        /// <summary>Set by Main: opens the lot.</summary>
        public Action Showroom;

        private static DialogueNode Node(string line)
        {
            return new DialogueNode("Hao", line);
        }

        public DialogueNode Root()
        {
            var first = _state == null || !_state.MetHao;

            if (first)
            {
                if (_state != null)
                {
                    _state.MetHao = true;
                    _state.Touch();
                }

                return Intro();
            }

            return Again();
        }

        // ---- the first time ----------------------------------------------------

        private DialogueNode Intro()
        {
            var node = Node(
                "Whatever you're selling, I ain't buying, I ain't smoking it and I ain't " +
                "holding it. This is a garage. You want something, it's got four wheels or " +
                "we're done.");

            node.Say("I'm looking at the cars.", () => WhatIsThis(),
                     "Ask what he's running here");

            node.Say("Who are you?", () => WhoHeIs(), "Ask him straight");

            node.Leave();
            return node;
        }

        private DialogueNode WhoHeIs()
        {
            var node = Node(
                "Hao. I build motors and I race 'em, and I been doing both since before you " +
                "could see over a steering wheel. Ask anybody who's run the canals at three " +
                "in the morning. They'll know the car even if they don't know me -- and " +
                "that's how I like it, honestly.");

            node.Say("So why the lot?", () => WhyTheLot());
            node.Say("What have you got?", () => Lot(), "See what's out front");
            node.Leave();
            return node;
        }

        private DialogueNode WhyTheLot()
        {
            var node = Node(
                "'Cause racing don't pay unless you're winning, and I'd rather eat. So I sell. " +
                "Everything out there runs, everything out there's set up properly -- I put " +
                "competition suspension under all of it before it goes on the lot, 'cause I'm " +
                "not having something leave here handling like a shopping trolley with my name " +
                "on the key.");

            node.Say("And where's it all from?", () => Provenance());
            node.Say("Show me.", () => Lot(), "See what's out front");
            node.Leave();
            return node;
        }

        private DialogueNode WhatIsThis()
        {
            var node = Node(
                "Everything you're looking at runs, and runs properly. I don't put anything " +
                "out there I wouldn't drive myself -- competition suspension under all of it, " +
                "set up by me, before it ever sees the front of the lot.");

            node.Say("Where's it all from?", () => Provenance());
            node.Say("What's it cost?", () => Lot(), "See what's out front");
            node.Leave();
            return node;
        }

        // ---- the actual business -----------------------------------------------

        private DialogueNode Provenance()
        {
            var node = Node(
                "That's the one question I don't answer, and you already know why you're " +
                "asking it.");

            node.Say("Alright. But I'm asking.", () => TheRealWork());
            node.Say("Forget I said anything.", () => Lot(), "See what's out front");
            node.Leave();
            return node;
        }

        /// <summary>
        /// The pitch, and the refusal.
        ///
        /// He lays out exactly what the future jobs are -- go and take something specific, he
        /// makes it disappear, you split it -- and then does not offer them, because a man who
        /// re-vins cars does not hand that to a stranger. The point is that the mechanic is
        /// explained long before it exists.
        /// </summary>
        private DialogueNode TheRealWork()
        {
            var node = Node(
                "Fine. A car's got a number stamped in the chassis, a number on the engine and " +
                "a number behind the glass, and all three have to agree or the thing's worth " +
                "nothing but parts. Making them agree again -- that's the job. That's the whole " +
                "job. Everybody thinks it's about the spray.");

            node.Say("So you re-vin them.", () => TheDeal());
            node.Say("That's a lot to tell a stranger.", () => WhyTelling());
            return node;
        }

        private DialogueNode WhyTelling()
        {
            var node = Node(
                "You're stood in a yard full of cars asking me where they came from. You " +
                "already worked it out. Me saying it out loud don't make you any more " +
                "dangerous than you were a minute ago.");

            node.Say("So what's the arrangement?", () => TheDeal());
            node.Leave();
            return node;
        }

        private DialogueNode TheDeal()
        {
            var node = Node(
                "Here's how it'd work, if it worked. I tell you a car. Not a type of car -- a " +
                "car, that one, that street, that time of night. You bring it here clean, no " +
                "chase, no cameras, no bodywork. It goes inside, it comes out somebody else's, " +
                "and we split what it sells for.");

            node.Say("Let's do one.", () => NotYet());
            node.Say("Why the specific car?", () => WhySpecific());
            return node;
        }

        private DialogueNode WhySpecific()
        {
            var node = Node(
                "'Cause I've already got the paperwork for that one sat in a drawer. That's " +
                "the part that takes weeks. The stealing's the easy bit -- anybody can steal a " +
                "car, that's why it don't pay.");

            node.Say("So when do we start?", () => NotYet());
            node.Leave();
            return node;
        }

        private DialogueNode NotYet()
        {
            var node = Node(
                "Not today. I've known you about four minutes and the first thing you did was " +
                "ask me where my cars come from. Buy something. Drive it. Come back in one " +
                "piece and don't have half of Davis behind you when you do, and then we'll " +
                "talk about the other thing.");

            node.Say("Fair enough.", () => Lot(), "See what's out front");
            node.Leave();
            return node;
        }

        // ---- afterwards --------------------------------------------------------

        private DialogueNode Again()
        {
            var bought = _state != null && _state.CarsBought.Count > 0;

            var node = Node(bought
                ? "Still running, is it? Good. Then you've had your money's worth already."
                : "Back again. You gonna stand there or you gonna buy something.");

            node.Say("Show me the lot.", () => Lot(), "See what's out front");

            node.Say("That other thing you mentioned.", () => StillNotYet(),
                     "Ask about the work");

            node.Leave();
            return node;
        }

        private DialogueNode StillNotYet()
        {
            var node = Node(
                "I ain't forgot. Neither have you, clearly. When I've got one worth doing I'll " +
                "find you -- and it'll be a car, an address and a window, and you'll have " +
                "about an hour of it.");

            node.Say("I'll be about.", () => null);
            node.Leave();
            return node;
        }

        private DialogueNode Lot()
        {
            var count = _hao == null ? 0 : _hao.Stock.Count;

            if (count == 0)
            {
                var empty = Node(
                    "Lot's bare. You've had the lot off me. Give me a bit and I'll have " +
                    "something else worth looking at.");

                empty.Leave();
                return empty;
            }

            // The screen is what he shows you; this is him agreeing to show it.
            Showroom?.Invoke();
            return null;
        }
    }
}
