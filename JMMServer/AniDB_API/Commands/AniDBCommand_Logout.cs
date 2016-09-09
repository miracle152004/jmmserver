﻿using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AniDBAPI.Commands
{
    public class AniDBCommand_Logout : AniDBUDPCommand, IAniDBUDPCommand
    {
        public virtual enHelperActivityType GetStartEventType()
        {
            return enHelperActivityType.LoggingOut;
        }

        public string GetKey()
        {
            return "Logout";
        }

        public virtual enHelperActivityType Process(ref Socket soUDP,
            ref IPEndPoint remoteIpEndPoint, string sessionID, Encoding enc)
        {
            ProcessCommand(ref soUDP, ref remoteIpEndPoint, sessionID, enc);

            return enHelperActivityType.LoggedOut;
        }

        public AniDBCommand_Logout()
        {
            commandType = enAniDBCommandType.Logout;
            commandID = "";
        }

        public void Init()
        {
            commandText = "LOGOUT ";
        }
    }
}