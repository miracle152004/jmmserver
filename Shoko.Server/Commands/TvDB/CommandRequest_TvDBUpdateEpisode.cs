﻿using System;
using System.Xml;
using Shoko.Commons.Extensions;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Models;
using Shoko.Server.Providers.TvDB;
using Shoko.Server.Repositories;

namespace Shoko.Server.Commands
{
    [Serializable]
    [Command(CommandRequestType.TvDB_UpdateEpisode)]
    public class CommandRequest_TvDBUpdateEpisode : CommandRequestImplementation
    {
        public int TvDBEpisodeID { get; set; }
        public bool ForceRefresh { get; set; }
        public bool DownloadImages { get; set; }
        public string InfoString { get; set; }

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority6;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            queueState = QueueStateEnum.GettingTvDBEpisode,
            extraParams = new[] {$"{InfoString} ({TvDBEpisodeID})"}
        };

        public CommandRequest_TvDBUpdateEpisode()
        {
        }

        public CommandRequest_TvDBUpdateEpisode(int tvDbEpisodeID, string infoString, bool downloadImages, bool forced)
        {
            TvDBEpisodeID = tvDbEpisodeID;
            ForceRefresh = forced;
            DownloadImages = downloadImages;
            InfoString = infoString;
            Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_TvDBUpdateEpisode: {0} ({1})", InfoString, TvDBEpisodeID);

            try
            {
                var ep = TvDBApiHelper.UpdateEpisode(TvDBEpisodeID, DownloadImages, ForceRefresh);
                if (ep == null) return;
                var xref = RepoFactory.CrossRef_AniDB_TvDB.GetByTvDBID(ep.SeriesID).DistinctBy(a => a.AniDBID);
                if (xref == null) return;
                foreach (var crossRefAniDbTvDbv2 in xref)
                {
                    var anime = RepoFactory.AnimeSeries.GetByAnimeID(crossRefAniDbTvDbv2.AniDBID);
                    if (anime == null) continue;
                    var episodes = RepoFactory.AnimeEpisode.GetBySeriesID(anime.AnimeSeriesID);
                    foreach (SVR_AnimeEpisode episode in episodes)
                    {
                        // Save
                        if ((episode.TvDBEpisode?.Id ?? TvDBEpisodeID) != TvDBEpisodeID) continue;
                        RepoFactory.AnimeEpisode.Save(episode);
                    }
                    anime.QueueUpdateStats();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error Processing CommandRequest_TvDBUpdateEpisode: {0} ({1})", InfoString, TvDBEpisodeID);
            }
        }

        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_TvDBUpdateEpisode{TvDBEpisodeID}";
        }

        public override bool LoadFromDBCommand(CommandRequest cq)
        {
            CommandID = cq.CommandID;
            CommandRequestID = cq.CommandRequestID;
            Priority = cq.Priority;
            CommandDetails = cq.CommandDetails;
            DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(CommandDetails);

                // populate the fields
                TvDBEpisodeID =
                    int.Parse(TryGetProperty(docCreator, "CommandRequest_TvDBUpdateEpisode", "TvDBEpisodeID"));
                ForceRefresh =
                    bool.Parse(TryGetProperty(docCreator, "CommandRequest_TvDBUpdateEpisode",
                        "ForceRefresh"));
                DownloadImages =
                    bool.Parse(TryGetProperty(docCreator, "CommandRequest_TvDBUpdateEpisode",
                        "DownloadImages"));
                InfoString =
                    TryGetProperty(docCreator, "CommandRequest_TvDBUpdateEpisode",
                        "InfoString");
            }

            return true;
        }

        public override CommandRequest ToDatabaseObject()
        {
            GenerateCommandID();

            CommandRequest cq = new CommandRequest
            {
                CommandID = CommandID,
                CommandType = CommandType,
                Priority = Priority,
                CommandDetails = ToXML(),
                DateTimeUpdated = DateTime.Now
            };
            return cq;
        }
    }
}
