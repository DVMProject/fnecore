// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 DVMProject Authors
*
*/

using System;

namespace fnecore
{
    /// <summary>
    /// Implements the FNE console patch status registry side channel.
    /// </summary>
    /// <remarks>
    /// This rides the metadata channel <see cref="FnePeer"/> already maintains for every connection
    /// (the same channel used for KEYS_INVENTORY/KEYS_UPDATE); it opens no channel of its own.
    /// </remarks>
    public partial class FnePeer
    {
        private uint patchStatusSequence = 0;
        private readonly object patchStatusSync = new object();

        /*
        ** Events
        */

        /// <summary>
        /// Event action that occurs when a patch status registry snapshot is received from the FNE.
        /// </summary>
        public event EventHandler<PatchStatusReceivedEvent> PatchStatusReceived;

        /*
        ** Methods
        */

        /// <summary>
        /// Helper to publish a complete console patch snapshot to the FNE.
        /// </summary>
        /// <param name="publish">Publish request. The sequence is assigned by this method.</param>
        /// <remarks>
        /// The FNE ignores a publish whose sequence does not advance past the sequence it already holds
        /// (see PatchStatusRegistry::publish), so every publish - including an unchanged keepalive - advances
        /// the sequence. The sequence is not reset across reconnects because the FNE retains the prior record
        /// until it expires.
        /// </remarks>
        public void PublishPatchStatus(PatchStatusPublish publish)
        {
            if (publish == null)
                throw new ArgumentNullException(nameof(publish));

            lock (patchStatusSync)
            {
                patchStatusSequence++;
                publish.Sequence = patchStatusSequence;
            }

            SendPatchStatusPublish(publish);
        }

        /// <summary>
        /// Helper to publish a patch status snapshot using an explicit sequence.
        /// </summary>
        /// <param name="publish">Publish request.</param>
        /// <param name="sequence">Sequence to send.</param>
        /// <remarks>
        /// The supplied sequence also advances the internal high-water mark used by
        /// <see cref="AllocatePatchStatusSequence"/> and <see cref="PublishPatchStatus"/>, so mixing this with
        /// the auto-incrementing publish path can never regress to a sequence the FNE would reject as stale.
        /// </remarks>
        public void PublishPatchStatusWithSequence(PatchStatusPublish publish, uint sequence)
        {
            if (publish == null)
                throw new ArgumentNullException(nameof(publish));

            lock (patchStatusSync)
            {
                if (sequence > patchStatusSequence)
                    patchStatusSequence = sequence;
            }

            publish.Sequence = sequence;

            SendPatchStatusPublish(publish);
        }

        /// <summary>
        /// Helper to request the current patch status registry snapshot from the FNE.
        /// </summary>
        public void RequestPatchStatus()
        {
            byte[] payload = PatchStatusCodec.EncodeRequest();
            byte[] message = PatchStatusCodec.WrapPayload(payload);

            SendMasterMetadata(CreateOpcode(Constants.NET_FUNC_TRANSFER, Constants.NET_TRANSFER_SUBFUNC_PATCH_STATUS),
                message, Constants.RtpCallEndSeq, CreateStreamID());
        }

        /// <summary>
        /// Allocates the next patch status sequence without sending anything.
        /// </summary>
        /// <returns>Sequence number to publish with.</returns>
        /// <remarks>
        /// This lets a caller record what it is about to publish before the publish goes out, so a registry
        /// snapshot that races the send is still matched against the correct publish.
        /// </remarks>
        public uint AllocatePatchStatusSequence()
        {
            lock (patchStatusSync)
            {
                patchStatusSequence++;
                return patchStatusSequence;
            }
        }

        /// <summary>
        /// Gets the patch status sequence most recently sent to the FNE.
        /// </summary>
        public uint CurrentPatchStatusSequence
        {
            get
            {
                lock (patchStatusSync)
                    return patchStatusSequence;
            }
        }

        /// <summary>
        /// Encodes and sends a patch status publish over the metadata channel.
        /// </summary>
        private void SendPatchStatusPublish(PatchStatusPublish publish)
        {
            publish.PeerId = peerId;

            byte[] payload = PatchStatusCodec.EncodePublish(publish);
            byte[] message = PatchStatusCodec.WrapPayload(payload);

            if (LogLevel == LogLevel.DEBUG)
            {
                Log(LogLevel.DEBUG, $"({systemName}) PEER {peerId} publishing patch status, sequence {publish.Sequence}, " +
                    $"patches {publish.Patches.Count}, ttl {publish.TtlSeconds}s");
            }

            SendMasterMetadata(CreateOpcode(Constants.NET_FUNC_TRANSFER, Constants.NET_TRANSFER_SUBFUNC_PATCH_STATUS),
                message, Constants.RtpCallEndSeq, CreateStreamID());
        }

        /// <summary>
        /// Handles a patch status transfer message received on the metadata channel.
        /// </summary>
        private void HandlePatchStatusMessage(byte[] message)
        {
            string json = PatchStatusCodec.ExtractPayload(message);
            PatchStatusRegistrySnapshot snapshot = PatchStatusCodec.DecodeRegistry(json);
            if (snapshot == null)
            {
                Log(LogLevel.ERROR, $"({systemName}) failed to parse patch status registry payload -- {json}");
                return;
            }

            PatchStatusReceived?.Invoke(this, new PatchStatusReceivedEvent(snapshot));
        }
    } // public partial class FnePeer
} // namespace fnecore
