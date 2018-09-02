﻿using System;
using System.Collections.Generic;
using System.Linq;
using NutzCode.InMemoryIndex;
using Shoko.Models.Server;
using Shoko.Server.Models;
using Shoko.Server.Repositories.ReaderWriterLockExtensions;


namespace Shoko.Server.Repositories.Repos
{
    public class AniDB_Anime_TagRepository : BaseRepository<AniDB_Anime_Tag, int>
    {
        private PocoIndex<int, AniDB_Anime_Tag, int> Animes;
        internal override int SelectKey(AniDB_Anime_Tag entity) => entity.AniDB_Anime_TagID;

        internal override void PopulateIndexes()
        {
            Animes = new PocoIndex<int, AniDB_Anime_Tag, int>(Cache, a => a.AnimeID);
        }

        internal override void ClearIndexes()
        {
            Animes = null;
        }

        public AniDB_Anime_Tag GetByAnimeIDAndTagID(int animeid, int tagid)
        {
            using (RepoLock.ReaderLock())
            {
                if (IsCached)
                    return Animes.GetMultiple(animeid).FirstOrDefault(a => a.TagID == tagid);
                return Table.FirstOrDefault(a => a.AnimeID == animeid && a.TagID == tagid);
            }
        }
        public List<AniDB_Anime_Tag> GetByAnimeID(int id)
        {
            using (RepoLock.ReaderLock())
            {
                if (IsCached)
                    return Animes.GetMultiple(id);
                return Table.Where(a => a.AnimeID == id).ToList();
            }
        }
        public List<int> GetIdsByAnimeID(int id)
        {
            using (RepoLock.ReaderLock())
            {
                if (IsCached)
                    return Animes.GetMultiple(id).Select(a=>a.AniDB_Anime_TagID).ToList();
                return Table.Where(a => a.AnimeID == id).Select(a => a.AniDB_Anime_TagID).ToList();
            }
        }
        public Dictionary<int, List<AniDB_Anime_Tag>> GetByAnimeIDs(IEnumerable<int> ids)
        {
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            using (RepoLock.ReaderLock())
            {
                if (IsCached)
                    return ids.ToDictionary(a => a, a => Animes.GetMultiple(a));
                return Table.Where(a => ids.Contains(a.AnimeID)).GroupBy(a=>a.AnimeID).ToDictionary(a => a.Key,a=>a.ToList());
            }
        }
        public Dictionary<int, List<int>> GetTagsIdByAnimeIDs(IEnumerable<int> ids)
        {
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            using (RepoLock.ReaderLock())
            {
                if (IsCached)
                    return ids.ToDictionary(a => a, a => Animes.GetMultiple(a).Select(b=>b.TagID).ToList());
                return Table.Where(a => ids.Contains(a.AnimeID)).GroupBy(a => a.AnimeID).ToDictionary(a => a.Key, a => a.Select(b=>b.TagID).ToList());
            }
        }
        /// <summary>
        /// Gets all the anime tags, but only if we have the anime locally
        /// </summary>
        /// <returns></returns>
        public List<AniDB_Anime_Tag> GetAllForLocalSeries()
        {
            return GetByAnimeIDs(Repo.AnimeSeries.GetAllAnimeIds()).SelectMany(a=>a.Value).Distinct().ToList();
        }

        public Dictionary<int, List<int>> GetGroupByTagIDAnimes()
        {
            using (RepoLock.ReaderLock())
            {
                return WhereAll().GroupBy(a => a.TagID).ToDictionary(a => a.Key, a => a.Select(b => b.AnimeID).ToList());
            }
        }

        internal List<AniDB_Anime_Tag> GetByTagID(int tagID)
        {
            using (RepoLock.ReaderLock())
            {
                return WhereAll().Where(a => a.TagID == tagID).ToList();
            }
        }

        internal List<SVR_AnimeSeries> GetAnimeWithTag(int tagID)
        {
            return Where(s => s.TagID == tagID).Select(s => Repo.AnimeSeries.GetByAnimeID(tagID)).Where(a => a != null).ToList();
        }
    }
}