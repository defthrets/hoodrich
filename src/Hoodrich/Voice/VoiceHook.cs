using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Voice
{
    /// <summary>
    /// The dialogue, said out loud, when there is a recording of it.
    ///
    /// STRICTLY ADDITIVE. Every subtitle still shows, every line still fires, and there is no
    /// state in which this layer stops something being said. With no manifest it does nothing
    /// at all -- not a file read, not a native call -- which is the state the mod ships in and
    /// the state it must behave identically in.
    ///
    /// The one thing it does take away is the GRUNT. Fifteen files in this mod call
    /// PLAY_PED_AMBIENT_SPEECH_NATIVE so a character's mouth moves under their subtitle: a
    /// GENERIC_YES standing in for words nobody recorded. Leave that in and a voiced line
    /// arrives with a stranger's grunt over the top of it. So Speak REPORTS whether it played,
    /// and the call sites make the noise only when it did not:
    ///
    ///     if (!VoiceHook.Speak(who, line, ped)) Mouth(ped);
    ///
    /// which is also why an unvoiced line behaves exactly as it did before this existed.
    /// </summary>
    internal static class VoiceHook
    {
        // ---- settings, set once by Main -----------------------------------------

        public static bool Enabled = true;
        public static float MasterVolume = 1f;
        public static bool DuckRadio = true;
        public static bool FacialAnimation = true;

        /// <summary>Whether there is anything to play at all.</summary>
        public static bool Active { get; private set; }

        // ---- how far a voice carries --------------------------------------------

        /// <summary>Full volume inside this, silent past the far one.</summary>
        private const float NearRange = 3f;
        private const float FarRange = 22f;

        /// <summary>How hard the pan swings. Never all the way, or a voice vanishes.</summary>
        private const float PanDepth = 0.45f;

        /// <summary>How often the volume is recomputed while a line plays.</summary>
        private const int TrackMs = 60;

        /// <summary>A mouth moving as if talking. Verified present in the game's own dicts.</summary>
        private const string FacialDict = "mp_facial";
        private const string FacialClip = "mic_chatter";

        private static readonly VoiceIndex Index = new VoiceIndex();

        private static Ped _from;
        private static int _startedAt;
        private static int _nextTrack;
        private static bool _ducked;

        /// <summary>
        /// Reads the catalogue, once, at startup.
        ///
        /// Failure here is not an error condition -- shipping without voice is the normal
        /// case. Active stays false and every entry point below turns into a null check.
        /// </summary>
        public static void Init(string audioDir, string manifestPath)
        {
            Active = false;

            try
            {
                if (!Enabled)
                {
                    Log.Info("Voice: switched off in the ini.");
                    return;
                }

                Active = Index.Load(manifestPath, audioDir);

                // Asked for once, here, rather than on the first line. REQUEST_ANIM_DICT is
                // asynchronous and a dict requested on the frame it is played arrives too
                // late for that frame -- the same trap the contact pictures fell into.
                if (Active && FacialAnimation)
                {
                    try { Function.Call(Hash.REQUEST_ANIM_DICT, FacialDict); }
                    catch { /* he will talk with a straight face */ }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Voice: could not start; dialogue stays silent.", ex);
                Active = false;
            }
        }

        /// <summary>
        /// Says the line if there is a recording of it.
        ///
        /// Returns whether it played, and the caller is expected to act on that -- see the
        /// class summary. Returning false is the normal, common answer.
        ///
        /// <paramref name="from"/> may be null, and then the line plays flat and centred:
        /// somebody talking to you rather than somebody talking to you from over there.
        /// </summary>
        public static bool Speak(string speaker, string line, Ped from)
        {
            if (!Active || !Enabled || string.IsNullOrEmpty(line)) return false;

            try
            {
                var path = Index.Find(speaker, line);
                if (path == null) return false;

                byte[] pcm;
                int rate;
                short channels, bits;

                if (!WaveOut.Read(path, out pcm, out rate, out channels, out bits)) return false;

                // Worked out BEFORE the buffer is handed over, so the first thing anybody
                // hears is already at the right volume for where they are stood.
                _from = from;
                float left, right;
                Mix(from, out left, out right);

                if (left <= 0.001f && right <= 0.001f) return false;   // too far to bother

                if (!WaveOut.Play(pcm, rate, channels, bits)) return false;

                WaveOut.Volume(left, right);

                _startedAt = Game.GameTime;
                _nextTrack = 0;

                Duck(true);
                Mouth(from);

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Voice: could not speak a line: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Per frame, and it costs a comparison when nothing is playing.
        ///
        /// Two jobs: free the buffer once Windows is done with it, and keep the volume honest
        /// while the player walks away mid-sentence. The second is the reason this is not
        /// fire-and-forget -- a voice that stays at full volume as you drive off is worse than
        /// one that was never positional.
        /// </summary>
        public static void Tick()
        {
            if (!Active) return;

            WaveOut.Poll();

            if (!WaveOut.Playing)
            {
                if (_ducked) Duck(false);
                _from = null;
                return;
            }

            var now = Game.GameTime;
            if (now < _nextTrack) return;
            _nextTrack = now + TrackMs;

            float left, right;
            Mix(_from, out left, out right);

            WaveOut.Volume(left, right);
        }

        /// <summary>Cuts the current line. For a conversation ending mid-sentence.</summary>
        public static void Silence()
        {
            if (!Active) return;

            WaveOut.Stop();
            Duck(false);
            _from = null;
        }

        /// <summary>
        /// Closes the device. MUST run on unload.
        ///
        /// An open waveOut handle with a prepared header outlives the managed side of a script
        /// reload, and after a few of those there are no devices left and audio stops working
        /// across the whole game with nothing in any log to explain it.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                Duck(false);
                WaveOut.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Debug("Voice: shutdown was untidy: " + ex.Message);
            }

            Active = false;
            _from = null;
        }

        /// <summary>
        /// How loud, and from which side, for where that man is stood.
        ///
        /// Measured from the CAMERA rather than from the player, because the camera is where
        /// the player's ears are as far as anybody watching is concerned -- swing the view
        /// round a man and his voice should cross the stereo field even though neither of you
        /// has moved.
        ///
        /// The pan never goes all the way to one side. A voice hard left is a voice half the
        /// people listening on speakers cannot hear.
        /// </summary>
        private static void Mix(Ped from, out float left, out float right)
        {
            var vol = Math.Max(0f, Math.Min(1f, MasterVolume));

            left = vol;
            right = vol;

            if (from == null || !from.Exists()) return;

            try
            {
                var ear = GameplayCamera.Position;
                var at = from.Position;

                var away = ear.DistanceTo(at);

                if (away >= FarRange) { left = right = 0f; return; }

                if (away > NearRange)
                {
                    var t = (away - NearRange) / (FarRange - NearRange);

                    // Squared, so it holds up close and falls away at the edge, which is how
                    // a voice actually behaves rather than how a straight line does.
                    vol *= (1f - t) * (1f - t);
                }

                // Which side of the camera he is on, as -1 to 1.
                // The camera's right, worked out rather than asked for. Cross the way it is
                // looking with world up and you have it, on any SHVDN build.
                var fwd = GameplayCamera.Direction;
                var flat = Vector3.Normalize(new Vector3(fwd.X, fwd.Y, 0f));
                var rightVec = new Vector3(flat.Y, -flat.X, 0f);

                var to = at - ear;
                var side = Vector3.Dot(Vector3.Normalize(new Vector3(to.X, to.Y, 0f)), rightVec);

                if (float.IsNaN(side)) side = 0f;

                var pan = Math.Max(-1f, Math.Min(1f, side)) * PanDepth;

                left = vol * (1f - Math.Max(0f, pan));
                right = vol * (1f + Math.Min(0f, pan));
            }
            catch
            {
                left = right = vol;
            }
        }

        /// <summary>
        /// Moves his mouth, without putting a second voice in it.
        ///
        /// The mod's own trick for this is an ambient speech line, which is a real recording
        /// of a real GTA character saying something else -- fine as a stand-in for words
        /// nobody has, and unusable underneath words somebody does. So a voiced line gets the
        /// facial animation on its own: the jaw moves, and the only thing you hear is the take.
        /// </summary>
        private static void Mouth(Ped who)
        {
            if (!FacialAnimation || who == null || !who.Exists() || !who.IsAlive) return;

            try
            {
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, who.Handle);

                Function.Call(Hash.PLAY_FACIAL_ANIM, who.Handle, FacialClip, FacialDict);
            }
            catch
            {
                // He says it with a straight face.
            }
        }

        /// <summary>
        /// Quietens the on-foot radio under a line, and puts it back after.
        ///
        /// ONLY the frontend radio, and only ever by toggling it. The obvious way to do this
        /// is to change the station, and the obvious way is how you end up leaving somebody's
        /// radio switched off after a crash mid-sentence -- a mod that silently turns off a
        /// feature of the base game and does not turn it back on is a mod nobody can diagnose.
        ///
        /// Restored unconditionally in Shutdown as well as here, so an unload mid-line puts it
        /// back too. And it is OFF by default in the ini: intelligibility is worth something,
        /// but not worth touching the player's radio without being asked.
        /// </summary>
        private static void Duck(bool down)
        {
            if (down == _ducked) return;
            if (down && !DuckRadio) return;

            try
            {
                Function.Call(Hash.SET_FRONTEND_RADIO_ACTIVE, !down);
            }
            catch
            {
                // The line is still audible over it.
            }

            _ducked = down;
        }
    }
}
