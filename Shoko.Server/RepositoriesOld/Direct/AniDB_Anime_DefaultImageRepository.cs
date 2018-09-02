﻿using System.Collections.Generic;
using Shoko.Models.Server;
using NHibernate.Criterion;
using Shoko.Server.Databases;
using Shoko.Server.Repositories.NHibernate;

namespace Shoko.Server.Repositories.Direct
{
    public class AniDB_Anime_DefaultImageRepository : BaseDirectRepository<AniDB_Anime_DefaultImage, int>
    {
        private AniDB_Anime_DefaultImageRepository()
        {
        }

        public static AniDB_Anime_DefaultImageRepository Create()
        {
            return new AniDB_Anime_DefaultImageRepository();
        }

        public AniDB_Anime_DefaultImage GetByAnimeIDAndImagezSizeType(int animeid, int imageType)
        {
            using (var session = DatabaseFactory.SessionFactory.OpenSession())
            {
                return GetByAnimeIDAndImagezSizeType(session.Wrap(), animeid, imageType);
            }
        }

        public AniDB_Anime_DefaultImage GetByAnimeIDAndImagezSizeType(ISessionWrapper session, int animeid,
            int imageType)
        {
            AniDB_Anime_DefaultImage cr = session
                .CreateCriteria(typeof(AniDB_Anime_DefaultImage))
                .Add(Restrictions.Eq("AnimeID", animeid))
                .Add(Restrictions.Eq("ImageType", imageType))
                .UniqueResult<AniDB_Anime_DefaultImage>();
            return cr;
        }

        public List<AniDB_Anime_DefaultImage> GetByAnimeID(int id)
        {
            using (var session = DatabaseFactory.SessionFactory.OpenSession())
            {
                var cats = session
                    .CreateCriteria(typeof(AniDB_Anime_DefaultImage))
                    .Add(Restrictions.Eq("AnimeID", id))
                    .List<AniDB_Anime_DefaultImage>();

                return new List<AniDB_Anime_DefaultImage>(cats);
            }
        }
    }
}