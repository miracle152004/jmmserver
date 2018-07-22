﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Shoko.Models;
using Shoko.Models.Server;
using NHibernate;
using NLog;
using Shoko.Server.Databases;
using Shoko.Server.Models;

namespace Shoko.Server.Repositories.Cached
{
    public class JMMUserRepository : BaseCachedRepository<SVR_JMMUser, int>
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private JMMUserRepository()
        {
        }

        public static JMMUserRepository Create()
        {
            var repo = new JMMUserRepository();
            RepoFactory.CachedRepositories.Add(repo);
            return repo;
        }

        protected override int SelectKey(SVR_JMMUser entity) => entity.JMMUserID;

        public override void PopulateIndexes()
        {
        }

        public override void RegenerateDb()
        {
        }

        public override void Save(SVR_JMMUser obj)
        {
            Save(obj, true);
        }

        public void Save(SVR_JMMUser obj, bool updateGroupFilters)
        {
            lock (obj)
            {
                bool isNew = false;
                if (obj.JMMUserID == 0)
                {
                    isNew = true;
                    base.Save(obj);
                }
                if (updateGroupFilters)
                {
                    using (var session = DatabaseFactory.SessionFactory.OpenSession())
                    {
                        SVR_JMMUser old = isNew ? null : session.Get<SVR_JMMUser>(obj.JMMUserID);
                        updateGroupFilters = SVR_JMMUser.CompareUser(old, obj);
                    }
                }
                base.Save(obj);
                if (updateGroupFilters)
                {
                    logger.Trace("Updating group filter stats by user from JMMUserRepository.Save: {0}", obj.JMMUserID);
                    obj.UpdateGroupFilters();
                }
            }
        }


        public SVR_JMMUser GetByUsername(string username)
        {
            lock (Cache)
            {
                return Cache.Values.FirstOrDefault(x =>
                    x.Username.Equals(username, StringComparison.InvariantCultureIgnoreCase));
            }
        }


        public List<SVR_JMMUser> GetAniDBUsers()
        {
            lock (Cache)
            {
                return Cache.Values.Where(a => a.IsAniDBUser == 1).ToList();
            }
        }

        public List<SVR_JMMUser> GetTraktUsers()
        {
            lock (Cache)
            {
                return Cache.Values.Where(a => a.IsTraktUser == 1).ToList();
            }
        }

        public SVR_JMMUser AuthenticateUser(string userName, string password)
        {
            if (password == null) password = string.Empty;
            string hashedPassword = Digest.Hash(password);
            lock (Cache)
            {
                return Cache.Values.FirstOrDefault(a =>
                    a.Username.Equals(userName, StringComparison.InvariantCultureIgnoreCase) &&
                    a.Password.Equals(hashedPassword));
            }
        }

        public long GetTotalRecordCount()
        {
            lock (Cache)
            {
                return Cache.Keys.Count;
            }
        }
    }
}
