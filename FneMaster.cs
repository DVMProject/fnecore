/**
* Digital Voice Modem - Fixed Network Equipment
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment
*
*/
/*
*   Copyright (C) 2022-2023 by Bryan Biedenkapp N2PLL
*
*   This program is free software: you can redistribute it and/or modify
*   it under the terms of the GNU Affero General Public License as published by
*   the Free Software Foundation, either version 3 of the License, or
*   (at your option) any later version.
*
*   This program is distributed in the hope that it will be useful,
*   but WITHOUT ANY WARRANTY; without even the implied warranty of
*   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
*   GNU Affero General Public License for more details.
*/

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json; // thanks .NET Core...

using fnecore.DMR;
using fnecore.P25;
using fnecore.NXDN;
using fnecore.EDAC;

namespace fnecore
{
    /// <summary>
    /// Event used to process incoming grant request data.
    /// </summary>
    public class GrantRequestEvent : EventArgs
    {
        /// <summary>
        /// Peer ID
        /// </summary>
        public uint PeerId { get; }
        /// <summary>
        /// DVM Mode
        /// </summary>
        public DVMState Mode { get; }
        /// <summary>
        /// Source Address
        /// </summary>
        public uint SrcId { get; }
        /// <summary>
        /// Destination Address
        /// </summary>
        public uint DstId { get; }
        /// <summary>
        /// Slot Number
        /// </summary>
        public byte Slot { get; }
        /// <summary>
        /// Flag indicating a unit-to-unit (private call) request.
        /// </summary>
        public bool UnitToUnit { get; }

        /*
        ** Methods
        */
        /// <summary>
        /// Initializes a new instance of the <see cref="GrantRequestEvent"/> class.
        /// </summary>
        private GrantRequestEvent()
        {
            /* stub */
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrantRequestEvent"/> class.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        /// <param name="mode">DVM Mode State</param>
        /// <param name="srcId">Source Address</param>
        /// <param name="dstId">Destination Address</param>
        /// <param name="slot">Slot Number</param>
        /// <param name="unitToUnit">Unit-to-Unit (Private Call)</param>
        public GrantRequestEvent(uint peerId, DVMState mode, uint srcId, uint dstId, byte slot, bool unitToUnit) : base()
        {
            this.PeerId = peerId;
            this.Mode = mode;
            this.SrcId = srcId;
            this.DstId = dstId;
            this.Slot = slot;
            this.UnitToUnit = unitToUnit;
        }
    } // public class GrantRequestEvent : EventArgs

    /// <summary>
    /// Implements an FNE "master".
    /// </summary>
    public class FneMaster : FneBase
    {
        private UdpListener server = null;

        private bool abortListening = false;

        private CancellationTokenSource listenCancelToken = new CancellationTokenSource();
        private Task listenTask = null;
        private CancellationTokenSource maintainenceCancelToken = new CancellationTokenSource();
        private Task maintainenceTask = null;

        private Dictionary<uint, PeerInformation> peers;

        /*
        ** Properties
        */

        /// <summary>
        /// Gets the <see cref="IPEndPoint"/> for this <see cref="FneMaster"/>.
        /// </summary>
        public IPEndPoint EndPoint
        {
            get
            {
                if (server != null)
                    return server.EndPoint;
                return null;
            }
        }

        /// <summary>
        /// Dictionary of connected peers.
        /// </summary>
        public Dictionary<uint, PeerInformation> Peers => peers;

        /// <summary>
        /// Gets/sets the password used for connecting to this master.
        /// </summary>
        public string Passphrase
        {
            get;
            set;
        }

        /// <summary>
        /// Flag indicating whether we are repeating to all connected peers.
        /// </summary>
        public bool Repeat
        {
            get;
            set;
        }

        /// <summary>
        /// Flag indicating whether we are repeating DMR to all connected peers.
        /// </summary>
        public bool NoRepeatDMR
        {
            get;
            set;
        }

        /// <summary>
        /// Flag indicating whether we are repeating P25 to all connected peers.
        /// </summary>
        public bool NoRepeatP25
        {
            get;
            set;
        }

        /// <summary>
        /// Flag indicating whether we are repeating NXDN to all connected peers.
        /// </summary>
        public bool NoRepeatNXDN
        {
            get;
            set;
        }

        /// <summary>
        /// Gets/sets how many pings are missed before we give up and force a reregister.
        /// </summary>
        public int MaxMissed
        {
            get;
            set;
        }

        /// <summary>
        /// Flag indicating whether peer activity transfers are allowed.
        /// </summary>
        public bool AllowActivityTransfer
        {
            get;
            set;
        }

        /// <summary>
        /// Flag indicating whether peer diagnostic transfers are allowed.
        /// </summary>
        public bool AllowDiagnosticTransfer
        {
            get;
            set;
        }

        /*
        ** Events/Callbacks
        */

        /// <summary>
        /// Event action that handles processing a grant request.
        /// </summary>
        public event EventHandler<GrantRequestEvent> GrantRequestReceived;

        /// <summary>
        /// Event action that handles peer activity transfers.
        /// </summary>
        public Action<uint, string> ActivityTransfer = null;

        /// <summary>
        /// Event action that handles peer diagnostic transfers.
        /// </summary>
        public Action<uint, string> DiagnosticTransfer = null;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="FneMaster"/> class.
        /// </summary>
        /// <param name="systemName"></param>
        /// <param name="peerId"></param>
        /// <param name="port"></param>
        public FneMaster(string systemName, uint peerId, int port) : this(systemName, peerId, new IPEndPoint(IPAddress.Any, port))
        {
            /* stub */
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FneMaster"/> class.
        /// </summary>
        /// <param name="systemName"></param>
        /// <param name="peerId"></param>
        /// <param name="endpoint"></param>
        public FneMaster(string systemName, uint peerId, IPEndPoint endpoint) : base(systemName, peerId)
        {
            fneType = FneType.MASTER;

            server = new UdpListener(endpoint);
            peers = new Dictionary<uint, PeerInformation>();

            Passphrase = string.Empty;
            Repeat = true;
            NoRepeatDMR = false;
            NoRepeatP25 = false;
            NoRepeatNXDN = false;
            AllowActivityTransfer = false;
            AllowDiagnosticTransfer = false;
        }

        /// <summary>
        /// Starts the main execution loop for this <see cref="FneMaster"/>.
        /// </summary>
        public override void Start()
        {
            if (isStarted)
                throw new InvalidOperationException("Cannot start listening when already started.");

            Logger(LogLevel.INFO, $"({systemName}) starting network services, {server.EndPoint}");

            abortListening = false;
            listenTask = Task.Factory.StartNew(Listen, listenCancelToken.Token);
            maintainenceTask = Task.Factory.StartNew(Maintainence, maintainenceCancelToken.Token);

            isStarted = true;
        }

        /// <summary>
        /// Stops the main execution loop for this <see cref="FneMaster"/>.
        /// </summary>
        public override void Stop()
        {
            if (!isStarted)
                throw new InvalidOperationException("Cannot stop listening when not started.");

            Logger(LogLevel.INFO, $"({systemName}) stopping network services, {server.EndPoint}");

            // stop UDP listen task
            if (listenTask != null)
            {
                abortListening = true;
                listenCancelToken.Cancel();

                try
                {
                    listenTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { /* stub */ }
                finally
                {
                    listenCancelToken.Dispose();
                }
            }

            // stop maintainence task
            if (maintainenceTask != null)
            {
                maintainenceCancelToken.Cancel();

                try
                {
                    maintainenceTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { /* stub */ }
                finally
                {
                    maintainenceCancelToken.Dispose();
                }
            }

            isStarted = false;
        }

        /// <summary>
        /// Helper to find and get <see cref="PeerInformation"/> for the given peer ID.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        /// <returns><see cref="PeerInformation"/> if connected, otherwise null.</returns>
        public PeerInformation FindPeer(uint peerId)
        {
            if (peers.ContainsKey(peerId))
                return peers[peerId];
            return null;
        }

        /// <summary>
        /// Helper to send a raw UDP frame.
        /// </summary>
        /// <param name="frame">UDP frame to send</param>
        public void Send(UdpFrame frame)
        {
            if (RawPacketTrace)
                Log(LogLevel.DEBUG, $"({systemName}) Network Sent (to {frame.Endpoint}) -- {FneUtils.HexDump(frame.Message, 0)}");

            server.Send(frame);
        }

        /// <summary>
        /// Helper to send a raw UDP frame.
        /// </summary>
        /// <param name="frame">UDP frame to send</param>
        public async void SendAsync(UdpFrame frame)
        {
            if (RawPacketTrace)
                Log(LogLevel.DEBUG, $"({systemName}) Network Sent (to {frame.Endpoint}) -- {FneUtils.HexDump(frame.Message, 0)}");

            await server.SendAsync(frame);
        }

        /// <summary>
        /// Helper to send a raw message to the specified peer.
        /// </summary>
        /// <param name="endpoint"><see cref="IPEndPoint"/></param>
        /// <param name="message">Byte array containing message to send</param>
        public void SendPeer(IPEndPoint endpoint, byte[] message)
        {
            Send(new UdpFrame()
            {
                Endpoint = endpoint,
                Message = message
            });
        }

        /// <summary>
        /// Helper to send a data message to the specified peer.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        /// <param name="opcode">Opcode</param>
        /// <param name="message">Byte array containing message to send</param>
        /// <param name="pktSeq"></param>
        /// <param name="streamId"></param>
        public void SendPeer(uint peerId, Tuple<byte, byte> opcode, byte[] message, ushort pktSeq, uint streamId = 0)
        {
            if (peers.ContainsKey(peerId))
            {
                if (streamId == 0)
                    streamId = peers[peerId].StreamID;
                byte[] data = WriteFrame(message, peerId, this.peerId, opcode, pktSeq, streamId);
                SendPeer(peers[peerId].EndPoint, data);
            }
        }

        /// <summary>
        /// Helper to send a data message to the specified peer.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        /// <param name="opcode">Opcode</param>
        /// <param name="message">Byte array containing message to send</param>
        /// <param name="incPktSeq"></param>
        public void SendPeer(uint peerId, Tuple<byte, byte> opcode, byte[] message, bool incPktSeq = false)
        {
            if (peers.ContainsKey(peerId))
            {
                if (incPktSeq) {
                    peers[peerId].PacketSequence = ++peers[peerId].PacketSequence;
                }

                SendPeer(peerId, opcode, message, peers[peerId].PacketSequence);
            }
        }

        /// <summary>
        /// Helper to send a command message to the specified peer.
        /// </summary>
        /// <param name="endpoint"><see cref="IPEndPoint"/></param>
        /// <param name="peerId">Peer ID</param>
        /// <param name="opcode">Opcode</param>
        /// <param name="pktSeq"></param>
        /// <param name="streamId"></param>
        /// <param name="message">Byte array containing message to send</param>
        public void SendPeerCommand(IPEndPoint endpoint, uint peerId, Tuple<byte, byte> opcode, ushort pktSeq, uint streamId, byte[] message = null)
        {
            int messageLength = 0;
            if (message != null)
                messageLength = message.Length;

            byte[] buffer = new byte[messageLength + 6];
            if (message != null)
                Buffer.BlockCopy(message, 0, buffer, 6, message.Length);

            byte[] frame = WriteFrame(buffer, peerId, this.peerId, opcode, pktSeq, streamId);
            SendPeer(endpoint, frame);
        }

        /// <summary>
        /// Helper to send a command message to the specified peer.
        /// </summary>
        /// <param name="endpoint"><see cref="IPEndPoint"/></param>
        /// <param name="peerId">Peer ID</param>
        /// <param name="opcode">Opcode</param>
        /// <param name="message">Byte array containing message to send</param>
        /// <param name="incPktSeq"></param>
        public void SendPeerCommand(uint peerId, Tuple<byte, byte> opcode, byte[] message = null, bool incPktSeq = false)
        {
            int messageLength = 0;
            if (message != null)
                messageLength = message.Length;

            byte[] buffer = new byte[messageLength + 6];
            if (message != null)
                Buffer.BlockCopy(message, 0, buffer, 6, message.Length);

            SendPeer(peerId, opcode, buffer, incPktSeq);
        }

        /// <summary>
        /// Helper to send a ACK response to the specified peer.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        public void SendPeerACK(uint peerId)
        {
            if (peers.ContainsKey(peerId))
            {
                peers[peerId].PacketSequence = ++peers[peerId].PacketSequence;

                // send ping response to peer
                SendPeerCommand(peerId, CreateOpcode(Constants.NET_FUNC_ACK));
            }
        }

        /// <summary>
        /// Helper to send a NAK response to the specified peer.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        /// <param name="tag">Tag NAK'ed</param>
        public void SendPeerNAK(uint peerId, string tag)
        {
            if (peers.ContainsKey(peerId))
            {
                peers[peerId].PacketSequence = ++peers[peerId].PacketSequence;

                // send ping response to peer
                SendPeerCommand(peerId, CreateOpcode(Constants.NET_FUNC_NAK));
                Log(LogLevel.WARNING, $"({systemName}) {tag} from unauth PEER {peerId}");
            }
        }

        /// <summary>
        /// Helper to send a NAK response to the specified endpoint.
        /// </summary>
        /// <param name="endpoint">IP endpoint</param>
        /// <param name="tag">Tag NAK'ed</param>
        public void SendNAK(IPEndPoint endpoint, uint peerId, string tag)
        {
            byte[] buffer = new byte[10];
            FneUtils.WriteBytes(peerId, ref buffer, 6);

            Send(new UdpFrame()
            {
                Endpoint = endpoint,
                Message = WriteFrame(buffer, peerId, this.peerId, CreateOpcode(Constants.NET_FUNC_NAK), 0, CreateStreamID())
            });
            Log(LogLevel.WARNING, $"({systemName}) {tag} from unconnected PEER {endpoint.Address.ToString()}:{endpoint.Port}");
        }

        /// <summary>
        /// Helper to send a raw message to the connected peers.
        /// </summary>
        /// <param name="opcode">Opcode</param>
        /// <param name="message">Byte array containing message to send</param>
        /// <param name="pktSeq">RTP Packet Sequence</param>
        public void SendPeers(Tuple<byte, byte> opcode, byte[] message, uint pktSeq = uint.MaxValue)
        {
            foreach (PeerInformation peer in peers.Values)
            {
                if (pktSeq > ushort.MaxValue)
                    pktSeq = peer.PacketSequence;

                byte[] data = WriteFrame(message, peer.PeerID, this.peerId, opcode, (ushort)pktSeq, peer.StreamID);
                SendAsync(new UdpFrame()
                {
                    Endpoint = peer.EndPoint,
                    Message = data
                });
            }
        }

        /// <summary>
        /// Internal helper to compare authorization hashes.
        /// </summary>
        /// <param name="message">FNE message frame</param>
        /// <param name="info">Peer Information</param>
        private bool CompareAuthHash(byte[] message, PeerInformation info)
        {
            // get the hash in the frame message
            byte[] hash = new byte[message.Length - 8];
            Buffer.BlockCopy(message, 8, hash, 0, hash.Length);

            // calculate our own hash
            byte[] inBuf = new byte[4 + Passphrase.Length];
            FneUtils.WriteBytes(info.Salt, ref inBuf, 0);
            FneUtils.StringToBytes(Passphrase, inBuf, 4, Passphrase.Length);
            byte[] outHash = FneUtils.sha256_hash(inBuf);

            // compare hashes
            if (hash.Length == outHash.Length)
            {
                bool res = true;
                for (int i = 0; i < hash.Length; i++)
                {
                    if (hash[i] != outHash[i])
                    {
                        res = false;
                        break;
                    }
                }

                return res;
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        private async void Listen()
        {
            CancellationToken ct = listenCancelToken.Token;
            ct.ThrowIfCancellationRequested();

            while (!abortListening)
            {
                try
                {
                    UdpFrame frame = await server.Receive();
                    if (RawPacketTrace)
                        Log(LogLevel.DEBUG, $"({systemName}) Network Received (from {frame.Endpoint}) -- {FneUtils.HexDump(frame.Message, 0)}");

                    // decode RTP frame
                    if (frame.Message.Length <= 0)
                        continue;

                    RtpHeader rtpHeader;
                    RtpFNEHeader fneHeader;
                    int messageLength = 0;
                    byte[] message = ReadFrame(frame, out messageLength, out rtpHeader, out fneHeader);
                    if (message == null)
                    {
                        Log(LogLevel.ERROR, $"({systemName}) Malformed packet (from {frame.Endpoint}); failed to decode RTP frame");
                        continue;
                    }

                    if (message.Length < 1)
                    {
                        Log(LogLevel.WARNING, $"({systemName}) Malformed packet (from {frame.Endpoint}) -- {FneUtils.HexDump(message, 0)}");
                        continue;
                    }

                    uint peerId = fneHeader.PeerID;
                    uint streamId = fneHeader.StreamID;

                    // update current peer stream ID
                    if (peerId > 0 && peers.ContainsKey(peerId) && streamId != 0)
                    {
                        ushort pktSeq = rtpHeader.Sequence;

                        if ((peers[peerId].StreamID == streamId) && (pktSeq != peers[peerId].NextPacketSequence))
                            Log(LogLevel.WARNING, $"({systemName}) PEER {peerId} Stream {streamId} out-of-sequence; {pktSeq} != {peers[peerId].NextPacketSequence}");

                        peers[peerId].StreamID = streamId;
                        peers[peerId].PacketSequence = pktSeq;
                        peers[peerId].NextPacketSequence = (ushort)(pktSeq + 1);
                        if (peers[peerId].NextPacketSequence > ushort.MaxValue)
                            peers[peerId].NextPacketSequence = 0;
                    }

                    // if we don't have a stream ID and are receiving call data -- throw an error and discard
                    if (streamId == 0 && fneHeader.Function == Constants.NET_FUNC_PROTOCOL)
                    {
                        Log(LogLevel.ERROR, $"({systemName}) PEER {peerId} Malformed packet (no stream ID for call?)");
                        continue;
                    }

                    // process incoming message frame opcodes
                    switch (fneHeader.Function)
                    {
                        case Constants.NET_FUNC_PROTOCOL:
                            {
                                if (fneHeader.SubFunction == Constants.NET_PROTOCOL_SUBFUNC_DMR)        // Encapsulated DMR data frame
                                {
                                    if (peerId > 0 && peers.ContainsKey(peerId))
                                    {
                                        // validate peer (simple validation really)
                                        if (peers[peerId].Connection && peers[peerId].EndPoint.ToString() == frame.Endpoint.ToString())
                                        {
                                            byte seqNo = message[4];
                                            uint srcId = FneUtils.Bytes3ToUInt32(message, 5);
                                            uint dstId = FneUtils.Bytes3ToUInt32(message, 8);

                                            byte bits = message[15];
                                            byte slot = (byte)(((bits & 0x80) == 0x80) ? 1 : 0);
                                            CallType callType = ((bits & 0x40) == 0x40) ? CallType.PRIVATE : CallType.GROUP;
                                            FrameType frameType = (FrameType)((bits & 0x30) >> 4);

                                            DMRDataType dataType = DMRDataType.IDLE;
                                            if ((bits & 0x20) == 0x20)
                                                dataType = (DMRDataType)(bits & ~(0x20));

                                            byte n = (byte)(bits & 0xF);

                                            // is the stream valid?
                                            bool ret = true;
                                            if (DMRDataValidate != null)
                                                ret = DMRDataValidate(peerId, srcId, dstId, slot, callType, frameType, dataType, streamId, message);

                                            if (ret)
                                            {
                                                // is this the peer being ignored?
                                                if (PeerIgnored != null)
                                                    ret = PeerIgnored(peerId, srcId, dstId, slot, callType, frameType, dataType, streamId);
                                                else
                                                    ret = false;

                                                if (ret)
                                                    continue;
#if DEBUG
                                                Log(LogLevel.DEBUG, $"{systemName} DMRD: SRC_PEER {peerId} SRC_ID {srcId} DST_ID {dstId} TS {slot} [STREAM ID {streamId}] PKT SEQ {rtpHeader.Sequence}");
#endif
                                                // are we repeating to connected peers?
                                                if (Repeat && !NoRepeatDMR)
                                                {
                                                    foreach (KeyValuePair<uint, PeerInformation> kvp in peers)
                                                    {
                                                        // don't repeat to the peer sending the data...
                                                        if (kvp.Key != peerId)
                                                        {
                                                            // is this peer being ignored
                                                            if (PeerIgnored != null)
                                                                ret = PeerIgnored(peerId, srcId, dstId, slot, callType, frameType, dataType, streamId);
                                                            else
                                                                ret = false;

                                                            if (!ret)
                                                                SendPeer(kvp.Key, CreateOpcode(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_DMR), message, rtpHeader.Sequence);
                                                        }
                                                    }
                                                }

                                                // perform any userland actions with the data
                                                FireDMRDataReceived(new DMRDataReceivedEvent(peerId, srcId, dstId, slot, callType, frameType, dataType, n, rtpHeader.Sequence, streamId, message));
                                            }
                                        }
                                        else
                                            SendPeerNAK(peerId, Constants.TAG_DMR_DATA);
                                    }
                                }
                                else if (fneHeader.SubFunction == Constants.NET_PROTOCOL_SUBFUNC_P25)   // Encapsulated P25 data frame
                                {
                                    if (peerId > 0 && peers.ContainsKey(peerId))
                                    {
                                        // validate peer (simple validation really)
                                        if (peers[peerId].Connection && peers[peerId].EndPoint.ToString() == frame.Endpoint.ToString())
                                        {
                                            uint srcId = FneUtils.Bytes3ToUInt32(message, 5);
                                            uint dstId = FneUtils.Bytes3ToUInt32(message, 8);
                                            CallType callType = (message[4] == P25Defines.LC_PRIVATE) ? CallType.PRIVATE : CallType.GROUP;
                                            P25DUID duid = (P25DUID)message[22];
                                            FrameType frameType = ((duid != P25DUID.TDU) && (duid != P25DUID.TDULC)) ? FrameType.VOICE : FrameType.TERMINATOR;

                                            // is the stream valid?
                                            bool ret = true;
                                            if (P25DataValidate != null)
                                                ret = P25DataValidate(peerId, srcId, dstId, callType, duid, frameType, streamId, message);

                                            if (ret)
                                            {
                                                // pre-process P25 data...
                                                FireP25DataPreprocess(new P25DataReceivedEvent(peerId, srcId, dstId, callType, duid, frameType, rtpHeader.Sequence, streamId, message));

                                                // is this the peer being ignored?
                                                if (PeerIgnored != null)
                                                    ret = PeerIgnored(peerId, srcId, dstId, 0, callType, frameType, (frameType == FrameType.VOICE) ? DMRDataType.VOICE_LC_HEADER : DMRDataType.TERMINATOR_WITH_LC, streamId);
                                                else
                                                    ret = false;

                                                if (ret)
                                                    continue;
#if DEBUG
                                                Log(LogLevel.DEBUG, $"{systemName} P25D: SRC_PEER {peerId} SRC_ID {srcId} DST_ID {dstId} [STREAM ID {streamId}] PKT SEQ {rtpHeader.Sequence}");
#endif
                                                // are we repeating to connected peers?
                                                if (Repeat && !NoRepeatP25)
                                                {
                                                    foreach (KeyValuePair<uint, PeerInformation> kvp in peers)
                                                    {
                                                        // don't repeat to the peer sending the data...
                                                        if (kvp.Key != peerId)
                                                        {
                                                            // is this peer being ignored
                                                            if (PeerIgnored != null)
                                                                ret = PeerIgnored(peerId, srcId, dstId, 0, callType, frameType, (frameType == FrameType.VOICE) ? DMRDataType.VOICE_LC_HEADER : DMRDataType.TERMINATOR_WITH_LC, streamId);
                                                            else
                                                                ret = false;

                                                            if (!ret)
                                                                SendPeer(kvp.Key, CreateOpcode(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), message, rtpHeader.Sequence);
                                                        }
                                                    }
                                                }

                                                // perform any userland actions with the data
                                                FireP25DataReceived(new P25DataReceivedEvent(peerId, srcId, dstId, callType, duid, frameType, rtpHeader.Sequence, streamId, message));
                                            }
                                        }
                                        else
                                            SendPeerNAK(peerId, Constants.TAG_P25_DATA);
                                    }
                                }
                                else if (fneHeader.SubFunction == Constants.NET_PROTOCOL_SUBFUNC_NXDN)  // Encapsulated NXDN data frame
                                {
                                    if (peerId > 0 && peers.ContainsKey(peerId))
                                    {
                                        // validate peer (simple validation really)
                                        if (peers[peerId].Connection && peers[peerId].EndPoint.ToString() == frame.Endpoint.ToString())
                                        {
                                            NXDNMessageType messageType = (NXDNMessageType)message[4];
                                            uint srcId = FneUtils.Bytes3ToUInt32(message, 5);
                                            uint dstId = FneUtils.Bytes3ToUInt32(message, 8);

                                            byte bits = message[15];
                                            CallType callType = ((bits & 0x40) == 0x40) ? CallType.PRIVATE : CallType.GROUP;
                                            FrameType frameType = (messageType != NXDNMessageType.MESSAGE_TYPE_TX_REL) ? FrameType.VOICE : FrameType.TERMINATOR;

                                            // is the stream valid?
                                            bool ret = true;
                                            if (NXDNDataValidate != null)
                                                ret = NXDNDataValidate(peerId, srcId, dstId, callType, messageType, frameType, streamId, message);

                                            if (ret)
                                            {
                                                // is this the peer being ignored?
                                                if (PeerIgnored != null)
                                                    ret = PeerIgnored(peerId, srcId, dstId, 0, callType, frameType, (frameType == FrameType.VOICE) ? DMRDataType.VOICE_LC_HEADER : DMRDataType.TERMINATOR_WITH_LC, streamId);
                                                else
                                                    ret = false;

                                                if (ret)
                                                    continue;
#if DEBUG
                                                Log(LogLevel.DEBUG, $"{systemName} NXDD: SRC_PEER {peerId} SRC_ID {srcId} DST_ID {dstId} [STREAM ID {streamId}] PKT SEQ {rtpHeader.Sequence}");
#endif
                                                // are we repeating to connected peers?
                                                if (Repeat && !NoRepeatNXDN)
                                                {
                                                    foreach (KeyValuePair<uint, PeerInformation> kvp in peers)
                                                    {
                                                        // don't repeat to the peer sending the data...
                                                        if (kvp.Key != peerId)
                                                        {
                                                            // is this peer being ignored
                                                            if (PeerIgnored != null)
                                                                ret = PeerIgnored(peerId, srcId, dstId, 0, callType, frameType, (frameType == FrameType.VOICE) ? DMRDataType.VOICE_LC_HEADER : DMRDataType.TERMINATOR_WITH_LC, streamId);
                                                            else
                                                                ret = false;

                                                            if (!ret)
                                                                SendPeer(kvp.Key, CreateOpcode(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_NXDN), message, rtpHeader.Sequence);
                                                        }
                                                    }
                                                }

                                                // perform any userland actions with the data
                                                FireNXDNDataReceived(new NXDNDataReceivedEvent(peerId, srcId, dstId, callType, messageType, frameType, rtpHeader.Sequence, streamId, message));
                                            }
                                        }
                                        else
                                            SendPeerNAK(peerId, Constants.TAG_NXDN_DATA);
                                    }
                                }
                                else
                                    Log(LogLevel.ERROR, $"({systemName}) Unknown protocol opcode {FneUtils.BytesToString(message, 0, 4)} -- {FneUtils.HexDump(message, 0)}");
                            }
                            break;

                        case Constants.NET_FUNC_RPTL:                                                   // Repeater Login
                            {
                                if (peerId > 0 && !peers.ContainsKey(peerId))
                                {
                                    PeerInformation info = new PeerInformation();
                                    info.PeerID = peerId;
                                    info.EndPoint = frame.Endpoint;
                                    info.PacketSequence = rtpHeader.Sequence;
                                    info.NextPacketSequence = ++rtpHeader.Sequence;
                                    info.StreamID = streamId;

                                    info.Salt = (uint)rand.Next(-2147483648, 2147483647);

                                    Log(LogLevel.INFO, $"({systemName}) Repeater logging in with PEER {peerId}, {info.EndPoint}");

                                    byte[] buffer = new byte[4];
                                    FneUtils.WriteBytes(info.Salt, ref buffer, 0);

                                    SendPeerCommand(frame.Endpoint, peerId, CreateOpcode(Constants.NET_FUNC_ACK), ++info.PacketSequence, streamId, buffer);

                                    info.State = ConnectionState.WAITING_AUTHORISATION;
                                    peers.Add(peerId, info);

                                    Log(LogLevel.INFO, $"({systemName}) Challenge Response sent to PEER {peerId} for login {info.Salt}");
                                }
                                else
                                {
                                    SendNAK(frame.Endpoint, peerId, Constants.TAG_REPEATER_LOGIN);

                                    // check if the peer is in our peer list -- if he is, and he isn't in a running state, reset
                                    // the login sequence
                                    if (peerId > 0 && !peers.ContainsKey(peerId))
                                    {
                                        PeerInformation info = new PeerInformation();
                                        if (info.State != ConnectionState.RUNNING)
                                            if (peers.ContainsKey(peerId))
                                                peers.Remove(peerId);
                                    }
                                }
                            }
                            break;
                        case Constants.NET_FUNC_RPTK:                                                   // Repeater Authentication
                            {
                                if (peerId > 0 && peers.ContainsKey(peerId))
                                {
                                    PeerInformation info = peers[peerId];
                                    info.LastPing = DateTime.Now;

                                    if (info.State == ConnectionState.WAITING_AUTHORISATION)
                                    {
                                        if (CompareAuthHash(message, info))
                                        {
                                            info.State = ConnectionState.WAITING_CONFIG;

                                            SendPeerACK(peerId);
                                            peers[peerId] = info;
                                            Log(LogLevel.INFO, $"({systemName}) PEER {peerId} has completed the login exchange");
                                        }
                                        else
                                        {
                                            Log(LogLevel.WARNING, $"({systemName}) PEER {peerId} has failed the login exchange");
                                            SendPeerNAK(peerId, Constants.TAG_REPEATER_AUTH);
                                            if (peers.ContainsKey(peerId))
                                                peers.Remove(peerId);
                                        }
                                    }
                                    else
                                    {
                                        Log(LogLevel.WARNING, $"({systemName}) PEER {peerId} tried login exchange in wrong state");
                                        SendPeerNAK(peerId, Constants.TAG_REPEATER_AUTH);
                                        if (peers.ContainsKey(peerId))
                                            peers.Remove(peerId);
                                    }
                                }
                                else
                                    SendNAK(frame.Endpoint, peerId, Constants.TAG_REPEATER_AUTH);
                            }
                            break;
                        case Constants.NET_FUNC_RPTC:                                                   // Repeater Configuration
                            {
                                if (peerId > 0 && peers.ContainsKey(peerId))
                                {
                                    PeerInformation info = peers[peerId];
                                    info.LastPing = DateTime.Now;

                                    if (info.State == ConnectionState.WAITING_CONFIG)
                                    {
                                        string payload = FneUtils.BytesToString(message, 8, message.Length - 8);
                                        try
                                        {
                                            JsonDocument json = JsonDocument.Parse(payload);

                                            // identity
                                            info.Details.Identity = json.RootElement.GetProperty("identity").GetString();
                                            info.Details.RxFrequency = json.RootElement.GetProperty("rxFrequency").GetUInt32();
                                            info.Details.TxFrequency = json.RootElement.GetProperty("txFrequency").GetUInt32();

                                            // system info
                                            JsonElement sysInfo = json.RootElement.GetProperty("info");
                                            info.Details.Latitude = sysInfo.GetProperty("latitude").GetDouble();
                                            info.Details.Longitude = sysInfo.GetProperty("longitude").GetDouble();
                                            info.Details.Height = sysInfo.GetProperty("height").GetInt32();
                                            info.Details.Location = sysInfo.GetProperty("location").GetString();

                                            // channel data
                                            JsonElement channel = json.RootElement.GetProperty("channel");
                                            info.Details.TxPower = channel.GetProperty("txPower").GetUInt32();
                                            info.Details.TxOffsetMhz = (float)channel.GetProperty("txOffsetMhz").GetDouble();
                                            info.Details.ChBandwidthKhz = (float)channel.GetProperty("chBandwidthKhz").GetDouble();
                                            info.Details.ChannelID = channel.GetProperty("channelId").GetByte();
                                            info.Details.ChannelNo = channel.GetProperty("channelNo").GetUInt32();

                                            // RCON
                                            JsonElement rcon = json.RootElement.GetProperty("rcon");
                                            info.Details.Password = rcon.GetProperty("password").GetString();
                                            info.Details.Port = rcon.GetProperty("port").GetInt32();

                                            info.Details.Software = json.RootElement.GetProperty("software").GetString();
                                        }
                                        catch
                                        {
                                            const string outOfDate = "Old/Out of Date Peer";

                                            // identity
                                            info.Details.Identity = outOfDate;
                                            info.Details.RxFrequency = 0;
                                            info.Details.TxFrequency = 0;

                                            // system info
                                            info.Details.Latitude = 0.0d;
                                            info.Details.Longitude = 0.0d;
                                            info.Details.Height = 0;
                                            info.Details.Location = outOfDate;

                                            // channel data
                                            info.Details.TxOffsetMhz = 0.0f;
                                            info.Details.ChBandwidthKhz = 0.0f;
                                            info.Details.ChannelID = 0;
                                            info.Details.ChannelNo = 0;
                                            info.Details.TxPower = 0;

                                            // RCON
                                            info.Details.Password = "ABCD1234";
                                            info.Details.Port = 9990; // default port

                                            info.Details.Software = "Peer Software Did Not Send JSON Configuration";
                                        }

                                        info.State = ConnectionState.RUNNING;
                                        info.Connection = true;
                                        info.PingsReceived = 0;
                                        info.LastPing = DateTime.Now;

                                        SendPeerACK(peerId);
                                        Log(LogLevel.INFO, $"({systemName}) PEER {peerId} has completed the configuration exchange");
                                        peers[peerId] = info;

                                        // userland actions
                                        FirePeerConnected(new PeerConnectedEvent(peerId, info));
                                    }
                                    else
                                    {
                                        Log(LogLevel.WARNING, $"({systemName}) PEER {peerId} tried configuration exchange in wrong state");
                                        SendPeerNAK(peerId, Constants.TAG_REPEATER_CONFIG);
                                        if (peers.ContainsKey(peerId))
                                            peers.Remove(peerId);
                                    }
                                }
                                else
                                    SendNAK(frame.Endpoint, peerId, Constants.TAG_REPEATER_CONFIG);
                            }
                            break;

                        case Constants.NET_FUNC_RPT_CLOSING:                                            // Repeater Closing (Disconnect)
                            {
                                if (peerId > 0 && peers.ContainsKey(peerId))
                                {
                                    // validate peer (simple validation really)
                                    if (peers[peerId].Connection && peers[peerId].EndPoint.ToString() == frame.Endpoint.ToString())
                                    {
                                        Log(LogLevel.INFO, $"({systemName}) PEER {peerId} is closing down");

                                        SendPeerACK(peerId);
                                        peers.Remove(peerId);

                                        // userland actions
                                        if (PeerDisconnected != null)
                                            PeerDisconnected(peerId);
                                    }
                                }
                            }
                            break;
                        case Constants.NET_FUNC_PING:                                                   // Repeater Ping
                            {
                                if (peerId > 0 && peers.ContainsKey(peerId))
                                {
                                    // validate peer (simple validation really)
                                    if (peers[peerId].Connection && peers[peerId].EndPoint.ToString() == frame.Endpoint.ToString())
                                    {
                                        PeerInformation peer = peers[peerId];
                                        peer.PingsReceived++;
                                        peer.LastPing = DateTime.Now;
                                        //peer.PacketSequence = ++peer.PacketSequence;

                                        peers[peerId] = peer;

                                        // send ping response to peer
                                        SendPeerCommand(peerId, CreateOpcode(Constants.NET_FUNC_PONG), null, true);
                                        Log(LogLevel.DEBUG, $"({systemName}) Received and answered RPTPING from PEER {peerId}");
                                    }
                                    else
                                        SendPeerNAK(peerId, Constants.TAG_REPEATER_PING);
                                }
                            }
                            break;

                        case Constants.NET_FUNC_GRANT:                                                  // Repeater Grant Request
                            {
                                if (peerId > 0 && peers.ContainsKey(peerId))
                                {
                                    // validate peer (simple validation really)
                                    if (peers[peerId].Connection && peers[peerId].EndPoint.ToString() == frame.Endpoint.ToString())
                                    {
                                        uint srcId = FneUtils.ToUInt32(message, 11);
                                        uint dstId = FneUtils.ToUInt32(message, 15);
                                        byte slot = (byte)(message[19] & 0x07);
                                        bool unitToUnit = (bool)((message[19] & 0x80) == 0x80);
                                        DVMState mode = (DVMState)message[20];

                                        if (GrantRequestReceived != null)
                                            GrantRequestReceived(this, new GrantRequestEvent(peerId, mode, srcId, dstId, slot, unitToUnit));
                                    }
                                    else
                                        SendPeerNAK(peerId, Constants.TAG_REPEATER_GRANT);
                                }
                            }
                            break;

                        case Constants.NET_FUNC_TRANSFER:
                            {
                                if (fneHeader.SubFunction == Constants.NET_TRANSFER_SUBFUNC_ACTIVITY)   // Peer Activity Log Transfer
                                {
                                    // can we do activity transfers?
                                    if (AllowActivityTransfer)
                                    {
                                        if (peerId > 0 && peers.ContainsKey(peerId))
                                        {
                                            // validate peer (simple validation really
                                            if (peers[peerId].Connection && peers[peerId].EndPoint.ToString() == frame.Endpoint.ToString())
                                            {
                                                byte[] buffer = new byte[messageLength - 11];
                                                Buffer.BlockCopy(message, 11, buffer, 0, buffer.Length);

                                                string msg = Encoding.ASCII.GetString(buffer);
                                                if (ActivityTransfer != null)
                                                    ActivityTransfer(peerId, msg);
                                            }
                                        }
                                    }
                                }
                                else if (fneHeader.SubFunction == Constants.NET_TRANSFER_SUBFUNC_DIAG)  // Peer Diagnostic Log Transfer
                                {
                                    // can we do diagnostic transfers?
                                    if (AllowDiagnosticTransfer)
                                    {
                                        if (peerId > 0 && peers.ContainsKey(peerId))
                                        {
                                            // validate peer (simple validation really)
                                            if (peers[peerId].Connection && peers[peerId].EndPoint.ToString() == frame.Endpoint.ToString())
                                            {
                                                byte[] buffer = new byte[messageLength - 11];
                                                Buffer.BlockCopy(message, 11, buffer, 0, buffer.Length);

                                                string msg = Encoding.ASCII.GetString(buffer);
                                                if (DiagnosticTransfer != null)
                                                    DiagnosticTransfer(peerId, msg);
                                            }
                                        }
                                    }
                                }
                                else
                                    Log(LogLevel.ERROR, $"({systemName}) Unknown transfer opcode {FneUtils.BytesToString(message, 0, 4)} -- {FneUtils.HexDump(message, 0)}");
                            }
                            break;

                        default:
                            Log(LogLevel.ERROR, $"({systemName}) Unknown opcode {FneUtils.BytesToString(message, 0, 4)} -- {FneUtils.HexDump(message, 0)}");
                            break;
                    }
                }
                catch (SocketException se)
                {
                    Log(LogLevel.FATAL, $"({systemName}) SOCKET ERROR: {se.SocketErrorCode}; {se.Message}");
                }

                if (ct.IsCancellationRequested)
                    abortListening = true;
            }
        }

        /// <summary>
        /// Internal maintainence routine.
        /// </summary>
        private async void Maintainence()
        {
            CancellationToken ct = maintainenceCancelToken.Token;
            while (!abortListening)
            {
                // check to see if any peers have been quiet (no ping) longer than allowed
                List<uint> peersToRemove = new List<uint>();
                foreach (KeyValuePair<uint, PeerInformation> kvp in peers)
                {
                    uint peerId = kvp.Key;
                    PeerInformation peer = kvp.Value;

                    DateTime dt = peer.LastPing.AddSeconds(PingTime * MaxMissed);
                    if (dt < DateTime.Now)
                    {
                        Log(LogLevel.INFO, $"({systemName}) PEER {peerId} has timed out");
                        peersToRemove.Add(peerId);
                    }
                }

                // remove any peers
                foreach (uint peerId in peersToRemove)
                    peers.Remove(peerId);

                try
                {
                    await Task.Delay(PingTime * 1000, ct);
                }
                catch (TaskCanceledException) { /* stub */ }
            }
        }
    } // public class FneMaster
} // namespace fnecore
