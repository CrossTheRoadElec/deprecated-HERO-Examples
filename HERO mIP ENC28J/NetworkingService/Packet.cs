// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using Microsoft.SPOT;

namespace Networking
{
    public class Packet
    {
        public Packet(PacketType type)
        {
            this.Type = type;
        }

        public PacketType Type { get; set; }

        public byte[] Content { get; internal set; }

        public uint SequenceNumber { get; internal set; }

        public Connection Socket { get; internal set; }

    }

    public enum PacketType { TCP, UDP };

}
