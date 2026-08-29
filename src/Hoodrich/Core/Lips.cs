using System;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Moving a face while a recorded line plays.
    ///
    /// THE GAME WILL NOT DO THIS FOR US. Its own lip sync is welded to its own speech system:
    /// PLAY_PED_AMBIENT_SPEECH picks a line out of the audio banks and animates the face to fit
    /// that line, and there is no way to hand it a file of ours. So the face and the sound are
    /// two separate things here, and all we can honestly do is have the mouth move for as long
    /// as the audio lasts.
    ///
    /// WHICH IS ENOUGH, because nobody is reading his lips. What breaks the illusion is a man
    /// standing perfectly still while his voice comes out of nowhere; what fixes it is any
    /// plausible mouth movement over the right span of time. Chatter is exactly that -- the
    /// facial the game uses for radio talk, where it never matched the words either.
    ///
    /// RETRIGGERED RATHER THAN LOOPED. The clip is a couple of seconds and most of these lines
    /// are twenty, so it is played again on a timer for as long as the audio is running. The
    /// timer is deliberately not the clip's exact length: restarting a facial slightly early
    /// reads as continuous talking, whereas letting it lapse and restart reads as a stutter.
    ///
    /// NOTHING IN HERE MAY THROW, for the same reason as the audio it follows. A face that does
    /// not move is a worse conversation, not a broken one.
    /// </summary>
    internal static class Lips
    {
        /// <summary>The chatter facial, which is the game's own "talking, words unknown".</summary>
        private const string Dict = "mp_facial";
        private const string Clip = "mic_chatter";

        /// <summary>What to put the face back to. Gendered, because the base sets are.</summary>
        private const string CalmMale = "facials@gen_male@base";
        private const string CalmFemale = "facials@gen_female@base";
        private const string CalmClip = "mood_normal_1";

        /// <summary>Shorter than the clip, so it never lapses between plays.</summary>
        private const int RetriggerMs = 1600;

        /// <summary>How often to ask MCI whether it is still going. Per frame is wasteful.</summary>
        private const int PollMs = 150;

        private static int _mouth;
        private static int _nextPlay;
        private static int _nextPoll;
        private static bool _talking;
        private static bool _asked;

        /// <summary>
        /// Called every frame while a conversation is open.
        ///
        /// The speaker is whoever the panel is quoting, which every caller already sets so the
        /// mugshot and the parting lines work -- so there is nothing new to wire up per screen.
        /// </summary>
        public static void Update(Ped speaker)
        {
            try
            {
                var now = Game.GameTime;

                // Asking MCI for its status is a P/Invoke and a string parse; sixty of those a
                // second to animate a face is not a good trade.
                if (now >= _nextPoll)
                {
                    _nextPoll = now + PollMs;
                    _talking = Voice.Talking;
                }

                if (!_talking || speaker == null || !speaker.Exists() || !speaker.IsAlive)
                {
                    Rest();
                    return;
                }

                if (!Ready()) return;

                if (speaker.Handle != _mouth || now >= _nextPlay)
                {
                    Function.Call(Hash.PLAY_FACIAL_ANIM, speaker.Handle, Clip, Dict);

                    _mouth = speaker.Handle;
                    _nextPlay = now + RetriggerMs;
                }
            }
            catch
            {
                // A still face is not worth taking the conversation down over.
            }
        }

        /// <summary>
        /// Put the face back, once, when the talking stops.
        ///
        /// The chatter clip would run itself out on its own, but "on its own" is up to two
        /// seconds after the sound stopped -- a man still mouthing at you in silence, which is
        /// worse than never having moved. Neutral cuts it off at the right moment.
        /// </summary>
        public static void Rest()
        {
            if (_mouth == 0) return;

            try
            {
                var ped = Ped.FromHandle(_mouth);

                if (ped != null && ped.Exists() && ped.IsAlive)
                {
                    var male = Function.Call<bool>(Hash.IS_PED_MALE, ped.Handle);
                    var calm = male ? CalmMale : CalmFemale;

                    Function.Call(Hash.REQUEST_ANIM_DICT, calm);

                    if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, calm))
                    {
                        Function.Call(Hash.PLAY_FACIAL_ANIM, ped.Handle, CalmClip, calm);
                    }
                }
            }
            catch
            {
                // Teardown.
            }

            _mouth = 0;
        }

        /// <summary>
        /// Whether the chatter dictionary is in memory yet.
        ///
        /// REQUEST_ANIM_DICT is asynchronous, so the first ask never succeeds and playing
        /// against an unloaded dictionary silently does nothing. Asked once and then only
        /// tested, which means the very first line of a session may go a frame or two before
        /// the face joins in, and every line after it is immediate.
        /// </summary>
        private static bool Ready()
        {
            if (!_asked)
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, Dict);
                _asked = true;
            }

            return Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, Dict);
        }
    }
}
