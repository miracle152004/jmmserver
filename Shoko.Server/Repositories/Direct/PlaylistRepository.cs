﻿using System.Collections.Generic;
using System.Linq;
using Shoko.Models.Server;
using NHibernate;
using Shoko.Server.Models;
using Shoko.Server.Repositories.NHibernate;

namespace Shoko.Server.Repositories.Direct
{
    public class PlaylistRepository : BaseDirectRepository<Playlist, int>
    {
        private PlaylistRepository()
        {
        }

        public static PlaylistRepository Create()
        {
            return new PlaylistRepository();
        }

        public override IReadOnlyList<Playlist> GetAll()
        {
            return base.GetAll().OrderBy(a => a.PlaylistName).ToList();
        }

        public override IReadOnlyList<Playlist> GetAll(ISession session)
        {
            return base.GetAll(session).OrderBy(a => a.PlaylistName).ToList();
        }

        public override IReadOnlyList<Playlist> GetAll(ISessionWrapper session)
        {
            return base.GetAll(session).OrderBy(a => a.PlaylistName).ToList();
        }
    }
}