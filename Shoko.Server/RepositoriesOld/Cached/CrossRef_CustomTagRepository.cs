﻿using System.Collections.Generic;
using System.Linq;
using Shoko.Models.Server;
using NutzCode.InMemoryIndex;
using Shoko.Models.Enums;

namespace Shoko.Server.Repositories
{
    public class CrossRef_CustomTagRepository : BaseCachedRepository<CrossRef_CustomTag, int>
    {
        private PocoIndex<int, CrossRef_CustomTag, int> Tags;
        private PocoIndex<int, CrossRef_CustomTag, int, int> Refs;

        private CrossRef_CustomTagRepository()
        {
        }

        protected override int SelectKey(CrossRef_CustomTag entity)
        {
            return entity.CrossRef_CustomTagID;
        }

        public override void PopulateIndexes()
        {
            Tags = new PocoIndex<int, CrossRef_CustomTag, int>(Cache, a => a.CustomTagID);
            Refs = new PocoIndex<int, CrossRef_CustomTag, int, int>(Cache, a => a.CrossRefID, a => a.CrossRefType);
        }

        public override void RegenerateDb()
        {
        }


        public static CrossRef_CustomTagRepository Create()
        {
            return new CrossRef_CustomTagRepository();
        }

        public List<CrossRef_CustomTag> GetByAnimeID(int id)
        {
            lock (Cache)
            {
                return Refs.GetMultiple(id, (int) CustomTagCrossRefType.Anime);
            }
        }


        public List<CrossRef_CustomTag> GetByCustomTagID(int id)
        {
            lock (Cache)
            {
                return Tags.GetMultiple(id);
            }
        }


        public List<CrossRef_CustomTag> GetByUniqueID(int customTagID, int crossRefType, int crossRefID)
        {
            lock (Cache)
            {
                return Refs.GetMultiple(crossRefID, crossRefType).Where(a => a.CustomTagID == customTagID).ToList();
            }
        }
    }
}