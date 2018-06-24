﻿using System;
using System.Collections.Generic;
using System.Xml;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Models;
using Shoko.Server.Repositories;

namespace Shoko.Server.Commands
{
    [Serializable]
    [Command(CommandRequestType.AniDB_GetUpdated)]
    public class CommandRequest_GetUpdated : CommandRequestImplementation
    {
        public virtual bool ForceRefresh { get; set; }

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority4;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            queueState = QueueStateEnum.GetUpdatedAnime,
            extraParams = new string[0]
        };

        public CommandRequest_GetUpdated()
        {
        }

        public CommandRequest_GetUpdated(bool forced)
        {
            ForceRefresh = forced;
            Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_GetUpdated");

            try
            {
                List<int> animeIDsToUpdate = new List<int>();

                // check the automated update table to see when the last time we ran this command
                ScheduledUpdate sched = Repo.ScheduledUpdate.GetByUpdateType((int) ScheduledUpdateType.AniDBUpdates);
                if (sched != null)
                {
                    int freqHours = Utils.GetScheduledHours(ServerSettings.AniDB_Anime_UpdateFrequency);

                    // if we have run this in the last 12 hours and are not forcing it, then exit
                    TimeSpan tsLastRun = DateTime.Now - sched.LastUpdate;
                    if (tsLastRun.TotalHours < freqHours)
                        if (!ForceRefresh) return;
                }


                long webUpdateTime = 0;
                long webUpdateTimeNew = 0;
                if (sched == null)
                {
                    // if this is the first time, lets ask for last 3 days
                    DateTime localTime = DateTime.Now.AddDays(-3);
                    DateTime utcTime = localTime.ToUniversalTime();
                    webUpdateTime = long.Parse(Commons.Utils.AniDB.AniDBDate(utcTime));
                    webUpdateTimeNew = long.Parse(Commons.Utils.AniDB.AniDBDate(DateTime.Now.ToUniversalTime()));

                }
                else
                {
                    logger.Trace("Last anidb info update was : {0}", sched.UpdateDetails);
                    webUpdateTime = long.Parse(sched.UpdateDetails);
                    webUpdateTimeNew = long.Parse(Commons.Utils.AniDB.AniDBDate(DateTime.Now.ToUniversalTime()));

                    logger.Info(
                        $"{Utils.FormatSecondsToDisplayTime(int.Parse((webUpdateTimeNew - webUpdateTime).ToString()))} since last UPDATED command");
                }

                // get a list of updates from AniDB
                // startTime will contain the date/time from which the updates apply to
                ShokoService.AnidbProcessor.GetUpdated(ref animeIDsToUpdate, ref webUpdateTime);

                using (var upd = Repo.ScheduledUpdate.BeginAddOrUpdate(()=> Repo.ScheduledUpdate.GetByUpdateType((int)ScheduledUpdateType.AniDBUpdates)))
                {
                    upd.Entity.UpdateType = (int) ScheduledUpdateType.AniDBUpdates;
                    upd.Entity.LastUpdate = DateTime.Now;
                    upd.Entity.UpdateDetails = webUpdateTimeNew.ToString();
                    sched=upd.Commit();
                }
                if (animeIDsToUpdate.Count == 0)
                {
                    logger.Info("No anime to be updated");
                    return;
                }


                int countAnime = 0;
                int countSeries = 0;
                foreach (int animeID in animeIDsToUpdate)
                {
                    // update the anime from HTTP
                    SVR_AniDB_Anime anime = Repo.AniDB_Anime.GetByAnimeID(animeID);
                    if (anime == null)
                    {
                        logger.Trace("No local record found for Anime ID: {0}, so skipping...", animeID);
                        continue;
                    }

                    logger.Info("Updating CommandRequest_GetUpdated: {0} ", animeID);

                    var update = RepoFactory.AniDB_AnimeUpdate.GetByAnimeID(animeID);

                    // but only if it hasn't been recently updated
                    TimeSpan ts = DateTime.Now - update.UpdatedAt;
                    if (ts.TotalHours > 4)
                    {
                        CommandRequest_GetAnimeHTTP cmdAnime = new CommandRequest_GetAnimeHTTP(animeID, true, false);
                        cmdAnime.Save();
                        countAnime++;
                    }

                    // update the group status
                    // this will allow us to determine which anime has missing episodes
                    // so we wonly get by an amime where we also have an associated series
                    SVR_AnimeSeries ser = Repo.AnimeSeries.GetByAnimeID(animeID);
                    if (ser != null)
                    {
                        CommandRequest_GetReleaseGroupStatus cmdStatus =
                            new CommandRequest_GetReleaseGroupStatus(animeID, true);
                        cmdStatus.Save();
                        countSeries++;
                    }
                }

                logger.Info("Updating {0} anime records, and {1} group status records", countAnime, countSeries);
            }
            catch (Exception ex)
            {
                logger.Error("Error processing CommandRequest_GetUpdated: {0}", ex);
            }
        }

        public override void GenerateCommandID()
        {
            CommandID = "CommandRequest_GetUpdated";
        }

        public override bool InitFromDB(Shoko.Models.Server.CommandRequest cq)
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
                ForceRefresh = bool.Parse(TryGetProperty(docCreator, "CommandRequest_GetUpdated", "ForceRefresh"));
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