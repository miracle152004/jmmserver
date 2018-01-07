﻿using System.Collections.Generic;
using System.Linq;
using NutzCode.InMemoryIndex;
using Shoko.Models.Server;

namespace Shoko.Server.Repositories.Repos
{
    public class CrossRef_Subtitles_AniDB_FileRepository : BaseRepository<CrossRef_Subtitles_AniDB_File, int>
    {
        private PocoIndex<int, CrossRef_Subtitles_AniDB_File, int> Files;

        internal override int SelectKey(CrossRef_Subtitles_AniDB_File entity) => entity.FileID;


        internal override void PopulateIndexes()
        {
            Files = new PocoIndex<int, CrossRef_Subtitles_AniDB_File, int>(Cache, a => a.FileID);
        }

        internal override void ClearIndexes()
        {
            Files = null;
        }

        public List<CrossRef_Subtitles_AniDB_File> GetByFileID(int id)
        {
            using (CacheLock.ReaderLock())
            {
                if (IsCached)
                    return Files.GetMultiple(id);
                return Table.Where(a => a.FileID == id).ToList();
            }
        }
        public List<int> GetIdsByFileID(int id)
        {
            using (CacheLock.ReaderLock())
            {
                if (IsCached)
                    return Files.GetMultiple(id).Select(a => a.CrossRef_Subtitles_AniDB_FileID).ToList();
                return Table.Where(a => a.FileID == id).Select(a => a.CrossRef_Subtitles_AniDB_FileID).ToList();
            }
        }
        public CrossRef_Subtitles_AniDB_File GetByFileAndLanguageID(int id, int langid)
        {
            using (CacheLock.ReaderLock())
            {
                if (IsCached)
                    return Files.GetMultiple(id).FirstOrDefault(a => a.LanguageID == langid);
                return Table.FirstOrDefault(a => a.FileID == id && a.LanguageID == langid);
            }
        }
        public List<int> GetIdsByFilesIDs(IEnumerable<int> ids)
        {
            using (CacheLock.ReaderLock())
            {
                if (IsCached)
                    return ids.SelectMany(a => Files.GetMultiple(a)).Select(a => a.FileID).Distinct().ToList();
                return Table.Where(a => ids.Contains(a.FileID)).Select(a => a.FileID).Distinct().ToList();
            }
        }
    }
}