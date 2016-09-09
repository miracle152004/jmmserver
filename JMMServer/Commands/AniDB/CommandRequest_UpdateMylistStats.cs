﻿using System;
using System.Globalization;
using System.Threading;
using System.Xml;
using JMMServer.Entities;
using JMMServer.Repositories;

namespace JMMServer.Commands.AniDB
{
    [Serializable]
    public class CommandRequest_UpdateMylistStats : CommandRequestImplementation, ICommandRequest
    {
        public bool ForceRefresh { get; set; }

        public CommandRequestPriority DefaultPriority
        {
            get { return CommandRequestPriority.Priority10; }
        }

        public QueueStateStruct PrettyDescription
        {
            get
            {
                return new QueueStateStruct() { queueState = QueueStateEnum.UpdateMyListStats, extraParams = new string[0] };
            }
        }

        public CommandRequest_UpdateMylistStats()
        {
        }

        public CommandRequest_UpdateMylistStats(bool forced)
        {
            this.ForceRefresh = forced;
            this.CommandType = (int) CommandRequestType.AniDB_UpdateMylistStats;
            this.Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_UpdateMylistStats");

            try
            {
                // we will always assume that an anime was downloaded via http first
                ScheduledUpdateRepository repSched = new ScheduledUpdateRepository();
                ScheduledUpdate sched = repSched.GetByUpdateType((int) ScheduledUpdateType.AniDBMylistStats);
                if (sched == null)
                {
                    sched = new ScheduledUpdate();
                    sched.UpdateType = (int) ScheduledUpdateType.AniDBMylistStats;
                    sched.UpdateDetails = "";
                }
                else
                {
                    int freqHours = Utils.GetScheduledHours(ServerSettings.AniDB_MyListStats_UpdateFrequency);

                    // if we have run this in the last 24 hours and are not forcing it, then exit
                    TimeSpan tsLastRun = DateTime.Now - sched.LastUpdate;
                    if (tsLastRun.TotalHours < freqHours)
                    {
                        if (!ForceRefresh) return;
                    }
                }

                sched.LastUpdate = DateTime.Now;
                repSched.Save(sched);

                JMMService.AnidbProcessor.UpdateMyListStats();
            }
            catch (Exception ex)
            {
                logger.Error("Error processing CommandRequest_UpdateMylistStats: {0}", ex.ToString());
                return;
            }
        }

        public override void GenerateCommandID()
        {
            this.CommandID = string.Format("CommandRequest_UpdateMylistStats");
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
                this.ForceRefresh =
                    bool.Parse(TryGetProperty(docCreator, "CommandRequest_UpdateMylistStats", "ForceRefresh"));
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