namespace Hoodrich.Gangs
{
    /// <summary>
    /// What people do at a party that nobody does on a gate.
    ///
    /// Every row is a clip the game ships: the dictionary, the clip, who may do it, and how it
    /// runs. WHO is "any", "male" or "female" -- the dances are the strip club's and read as
    /// somebody dancing on a woman and as a joke on a man, and the celebration gestures come in
    /// a male and a female cut that only look right on their own sex. HOW is "loop" for a thing
    /// that can go on for the length of a break, and "once" for a thing that happens -- being
    /// sick, waking up on the floor -- and then is over, after which he stands there until his
    /// break ends and he goes back to his mark.
    ///
    /// Every name here was checked against the animation list on the machine this was written
    /// on. A row this install has not got simply never starts: the break becomes a cigarette.
    /// </summary>
    internal static class PartyClips
    {
        public static readonly string[][] Lamars =
        {
            // Everybody: the signs and the gestures.
            new[] { "mp_player_int_uppergang_sign_a", "mp_player_int_gang_sign_a", "any", "loop" },
            new[] { "mp_player_int_uppergang_sign_b", "mp_player_int_gang_sign_b", "any", "loop" },
            new[] { "mp_player_int_upperpeace_sign", "mp_player_int_peace_sign", "any", "loop" },
            new[] { "cellphone@self@franklin@", "peace", "any", "loop" },
            new[] { "anim@mp_player_intcelebrationmale@slow_clap", "slow_clap", "male", "loop" },
            new[] { "anim@mp_player_intcelebrationfemale@slow_clap", "slow_clap", "female", "loop" },
            new[] { "anim@mp_player_intcelebrationmale@face_palm", "face_palm", "male", "loop" },
            new[] { "anim@mp_player_intcelebrationfemale@face_palm", "face_palm", "female", "loop" },
            new[] { "anim@mp_player_intcelebrationmale@air_guitar", "air_guitar", "male", "loop" },
            new[] { "anim@mp_player_intcelebrationfemale@air_guitar", "air_guitar", "female", "loop" },
            new[] { "anim@mp_player_intcelebrationmale@raise_the_roof", "raise_the_roof", "male", "loop" },
            new[] { "anim@mp_player_intcelebrationfemale@raise_the_roof", "raise_the_roof", "female", "loop" },
            new[] { "anim@mp_player_intcelebrationmale@dj", "dj", "male", "loop" },
            new[] { "anim@mp_player_intcelebrationmale@rock", "rock", "male", "loop" },

            // The women dance.
            new[] { "mini@strip_club@private_dance@part1", "priv_dance_p1", "female", "loop" },
            new[] { "mini@strip_club@private_dance@part2", "priv_dance_p2", "female", "loop" },
            new[] { "mini@strip_club@private_dance@part3", "priv_dance_p3", "female", "loop" },
            new[] { "mini@strip_club@private_dance@idle", "priv_dance_idle", "female", "loop" },
            new[] { "mini@strip_club@backroom@", "stripper_b_backroom_idle_b", "female", "loop" },
            new[] { "mini@strip_club@lap_dance@ld_girl_a_song_a_p2", "ld_girl_a_song_a_p2_f", "female", "loop" },
            new[] { "mini@strip_club@lap_dance_2g@ld_2g_p1", "ld_2g_p1_s2", "female", "loop" },
            new[] { "mini@strip_club@lap_dance_2g@ld_2g_p2", "ld_2g_p2_s2", "female", "loop" },
            new[] { "mp_safehouse", "lap_dance_girl", "female", "loop" },

            // The men, a few drinks in.
            new[] { "switch@trevor@mocks_lapdance", "001443_01_trvs_28_idle_stripper", "male", "loop" },
            new[] { "mp_player_intwank", "mp_player_int_wank", "male", "loop" },
            new[] { "missbigscore1switch_trevor_piss", "piss_loop", "male", "loop" },
            new[] { "missheistpaletoscore1leadinout", "trv_puking_leadout", "male", "once" },
            new[] { "random@peyote@chicken", "wakeup", "any", "once" },
            new[] { "random@peyote@bird", "wakeup", "any", "once" },
            new[] { "switch@trevor@scares_tramp", "trev_scares_tramp_idle_tramp", "any", "loop" },
        };
    }
}
