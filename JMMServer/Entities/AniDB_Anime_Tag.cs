﻿using AniDBAPI;

namespace JMMServer.Entities
{
    public class AniDB_Anime_Tag
    {
        public int AniDB_Anime_TagID { get; private set; }
        public int AnimeID { get; set; }
        public int TagID { get; set; }
        public int Approval { get; set; }
        public int Weight { get; set; }

        public void Populate(Raw_AniDB_Tag rawTag)
        {
            this.AnimeID = rawTag.AnimeID;
            this.TagID = rawTag.TagID;
            this.Approval = 100;
            this.Weight = rawTag.Weight;
        }
    }
}