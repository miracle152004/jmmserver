﻿using System;
using System.Collections.Generic;
using System.Linq;
using JMMServer.Collections;
using JMMServer.Entities;
using JMMServer.Repositories.NHibernate;
using NHibernate;
using NHibernate.Criterion;

namespace JMMServer.Repositories
{
    public class TvDB_ImageFanartRepository
    {
        public void Save(TvDB_ImageFanart obj)
        {
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                // populate the database
                using (var transaction = session.BeginTransaction())
                {
                    session.SaveOrUpdate(obj);
                    transaction.Commit();
                }
            }
        }

        public TvDB_ImageFanart GetByID(int id)
        {
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                return GetByID(session.Wrap(), id);
            }
        }

        public TvDB_ImageFanart GetByID(ISessionWrapper session, int id)
        {
            return session.Get<TvDB_ImageFanart>(id);
        }

        public TvDB_ImageFanart GetByTvDBID(int id)
        {
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                TvDB_ImageFanart cr = session
                    .CreateCriteria(typeof(TvDB_ImageFanart))
                    .Add(Restrictions.Eq("Id", id))
                    .UniqueResult<TvDB_ImageFanart>();
                return cr;
            }
        }

        public List<TvDB_ImageFanart> GetBySeriesID(int seriesID)
        {
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                return GetBySeriesID(session.Wrap(), seriesID);
            }
        }

        public List<TvDB_ImageFanart> GetBySeriesID(ISessionWrapper session, int seriesID)
        {
            var objs = session
                .CreateCriteria(typeof(TvDB_ImageFanart))
                .Add(Restrictions.Eq("SeriesID", seriesID))
                .List<TvDB_ImageFanart>();

            return new List<TvDB_ImageFanart>(objs);
        }

        public ILookup<int, TvDB_ImageFanart> GetByAnimeIDs(ISessionWrapper session, int[] animeIds)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (animeIds == null)
                throw new ArgumentNullException(nameof(animeIds));

            if (animeIds.Length == 0)
            {
                return EmptyLookup<int, TvDB_ImageFanart>.Instance;
            }

            var fanartByAnime = session.CreateSQLQuery(@"
                SELECT DISTINCT crAdbTvTb.AnimeID, {tvdbFanart.*}
                   FROM CrossRef_AniDB_TvDBV2 AS crAdbTvTb
                      INNER JOIN TvDB_ImageFanart AS tvdbFanart
                         ON tvdbFanart.SeriesID = crAdbTvTb.TvDBID
                   WHERE crAdbTvTb.AnimeID IN (:animeIds)")
                .AddScalar("AnimeID", NHibernateUtil.Int32)
                .AddEntity("tvdbFanart", typeof(TvDB_ImageFanart))
                .SetParameterList("animeIds", animeIds)
                .List<object[]>()
                .ToLookup(r => (int)r[0], r => (TvDB_ImageFanart)r[1]);

            return fanartByAnime;
        }

        public List<TvDB_ImageFanart> GetAll()
        {
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                var objs = session
                    .CreateCriteria(typeof(TvDB_ImageFanart))
                    .List<TvDB_ImageFanart>();

                return new List<TvDB_ImageFanart>(objs);
            }
        }

        public void Delete(int id)
        {
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                // populate the database
                using (var transaction = session.BeginTransaction())
                {
                    TvDB_ImageFanart cr = GetByID(id);
                    if (cr != null)
                    {
                        session.Delete(cr);
                        transaction.Commit();
                    }
                }
            }
        }
    }
}