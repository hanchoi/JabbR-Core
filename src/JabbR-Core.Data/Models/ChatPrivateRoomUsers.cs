﻿using System;
using System.Collections.Generic;

namespace JabbR_Core.Data.Models
{
    public partial class ChatPrivateRoomUsers
    {
        public int ChatRoomKey { get; set; }
        public int ChatUserKey { get; set; }

        public ChatRoom ChatRoomKeyNavigation { get; set; }
        public ChatUser ChatUserKeyNavigation { get; set; }
    }
}
