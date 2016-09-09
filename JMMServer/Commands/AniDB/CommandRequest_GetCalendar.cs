﻿using System;
using System.Globalization;
using System.Threading;
using System.Xml;
using AniDBAPI;
using JMMServer.Entities;
using JMMServer.Repositories;

namespace JMMServer.Commands
{
    [Serializable]
    public class CommandRequest_GetCalendar : CommandRequestImplementation, ICommandRequest
    {
        public bool ForceRefresh { get; set; }

        public CommandRequestPriority DefaultPriority
        {
            get { return CommandRequestPriority.Priority7; }
        }

        public QueueStateStruct PrettyDescription
        {
            get
            {
                return new QueueStateStruct() { queueState = QueueStateEnum.GetCalendar, extraParams = new string[0] };
            }
        }

        public CommandRequest_GetCalendar()
        {
        }

        public CommandRequest_GetCalendar(bool forced)
        {
            this.ForceRefresh = forced;
            this.CommandType = (int) CommandRequestType.AniDB_GetCalendar;
            this.Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_GetCalendar");

            try
            {
                // we will always assume that an anime was downloaded via http first
                ScheduledUpdateRepository repSched = new ScheduledUpdateRepository();
                AniDB_AnimeRepository repAnime = new AniDB_AnimeRepository();

                ScheduledUpdate sched = repSched.GetByUpdateType((int) ScheduledUpdateType.AniDBCalendar);
                if (sched == null)
                {
                    sched = new ScheduledUpdate();
                    sched.UpdateType = (int) ScheduledUpdateType.AniDBCalendar;
                    sched.UpdateDetails = "";
                }
                else
                {
                    int freqHours = Utils.GetScheduledHours(ServerSettings.AniDB_Calendar_UpdateFrequency);

                    // if we have run this in the last 12 hours and are not forcing it, then exit
                    TimeSpan tsLastRun = DateTime.Now - sched.LastUpdate;
                    if (tsLastRun.TotalHours < freqHours)
                    {
                        if (!ForceRefresh) return;
                    }
                }

                sched.LastUpdate = DateTime.Now;
                repSched.Save(sched);

                CalendarCollection colCalendars = JMMService.AnidbProcessor.GetCalendarUDP();
                if (colCalendars == null || colCalendars.Calendars == null)
                {
                    logger.Error("Could not get calendar from AniDB");
                    return;
                }
                foreach (AniDBAPI.Calendar cal in colCalendars.Calendars)
                {
                    AniDB_Anime anime = repAnime.GetByAnimeID(cal.AnimeID);
                    if (anime != null)
                    {
                        // don't update if the local data is less 2 days old
                        TimeSpan ts = DateTime.Now - anime.DateTimeUpdated;
                        if (ts.TotalDays >= 2)
                        {
                            CommandRequest_GetAnimeHTTP cmdAnime = new CommandRequest_GetAnimeHTTP(cal.AnimeID, true,
                                false);
                            cmdAnime.Save();
                        }
                        else
                        {
                            // update the release date even if we don't update the anime record
                            if (anime.AirDate != cal.ReleaseDate)
                            {
                                anime.AirDate = cal.ReleaseDate;
                                repAnime.Save(anime);
                                AnimeSeriesRepository srepo = new AnimeSeriesRepository();
                                AnimeSeries ser = srepo.GetByAnimeID(anime.AnimeID);
                                if (ser != null)
                                    srepo.Save(ser, true, false);
                            }
                        }
                    }
                    else
                    {
                        CommandRequest_GetAnimeHTTP cmdAnime = new CommandRequest_GetAnimeHTTP(cal.AnimeID, true, false);
                        cmdAnime.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.ErrorException("Error processing CommandRequest_GetCalendar: " + ex.ToString(), ex);
                return;
            }
        }

        public override void GenerateCommandID()
        {
            this.CommandID = string.Format("CommandRequest_GetCalendar");
        }

        public override bool LoadFromDBCommand(CommandRequest cq)
        {
            this.CommandID = cq.CommandID;
            this.CommandRequestID = cq.CommandRequestID;
            this.CommandType = cq.CommandType;
            this.Priority = cq.Priority;
            this.CommandDetails = cq.CommandDetails;
            this.DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (this.CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(this.CommandDetails);

                // populate the fields
                this.ForceRefresh = bool.Parse(TryGetProperty(docCreator, "CommandRequest_GetCalendar", "ForceRefresh"));
            }

            return true;
        }

        public override CommandRequest ToDatabaseObject()
        {
            GenerateCommandID();

            CommandRequest cq = new CommandRequest();
            cq.CommandID = this.CommandID;
            cq.CommandType = this.CommandType;
            cq.Priority = this.Priority;
            cq.CommandDetails = this.ToXML();
            cq.DateTimeUpdated = DateTime.Now;

            return cq;
        }
    }
}