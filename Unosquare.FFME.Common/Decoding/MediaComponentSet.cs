﻿namespace Unosquare.FFME.Decoding
{
    using Primitives;
    using Shared;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Represents a set of Audio, Video and Subtitle components.
    /// This class is useful in order to group all components into
    /// a single set. Sending packets is automatically handled by
    /// this class. This class is thread safe.
    /// </summary>
    internal sealed class MediaComponentSet : IDisposable
    {
        #region Private Declarations

        /// <summary>
        /// The synchronize lock
        /// </summary>
        private readonly object SyncLock = new object();

        /// <summary>
        /// To detect redundant Dispose calls
        /// </summary>
        private readonly AtomicBoolean m_IsDisposed = new AtomicBoolean(false);

        private ReadOnlyCollection<MediaComponent> m_All = new ReadOnlyCollection<MediaComponent>(new List<MediaComponent>(0));

        private ReadOnlyCollection<MediaType> m_MediaTypes = new ReadOnlyCollection<MediaType>(new List<MediaType>(0));

        private int m_Count = default;

        private MediaType m_MainMediaType = MediaType.None;

        private MediaComponent m_Main = null;

        private AudioComponent m_Audio = null;

        private VideoComponent m_Video = null;

        private SubtitleComponent m_Subtitle = null;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaComponentSet"/> class.
        /// </summary>
        internal MediaComponentSet()
        {
            // prevent external initialization
        }

        #endregion

        #region Delegates

        public delegate void OnPacketsClearedDelegate();
        public delegate void OnPacketQueuedDelegate(IntPtr avPacket, MediaType mediaType, long packetBufferLength, int packetBufferCount);
        public delegate void OnPacketDequeuedDelegate(IntPtr avPacket, MediaType mediaType, long packetBufferLength, int packetBufferCount);
        public delegate void OnFrameDecodedDelegate(IntPtr avFrame, MediaType mediaType);
        public delegate void OnSubtitleDecodedDelegate(IntPtr avSubititle);

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets a method that gets called when a packet is queued.
        /// </summary>
        public OnPacketQueuedDelegate OnPacketQueued { get; set; }

        /// <summary>
        /// Gets or sets a method that gets called when a packet is removed from the queue.
        /// </summary>
        public OnPacketDequeuedDelegate OnPacketDequeued { get; set; }

        /// <summary>
        /// Gets or sets a method that gets called when all component packet queues are cleared.
        /// </summary>
        public OnPacketsClearedDelegate OnPacketsCleared { get; set; }

        /// <summary>
        /// Gets or sets a method that gets called when an audio or video frame gets decoded.
        /// </summary>
        public OnFrameDecodedDelegate OnFrameDecoded { get; set; }

        /// <summary>
        /// Gets or sets a method that gets called when a subtitle frame gets decoded.
        /// </summary>
        public OnSubtitleDecodedDelegate OnSubtitleDecoded { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed => m_IsDisposed.Value;

        /// <summary>
        /// Gets the registred component count.
        /// </summary>
        public int Count
        {
            get { lock (SyncLock) return m_Count; }
        }

        /// <summary>
        /// Gets the available component media types.
        /// </summary>
        public ReadOnlyCollection<MediaType> MediaTypes
        {
            get { lock (SyncLock) return m_MediaTypes; }
        }

        /// <summary>
        /// Gets all the components in a read-only collection.
        /// </summary>
        public ReadOnlyCollection<MediaComponent> All
        {
            get { lock (SyncLock) return m_All; }
        }

        /// <summary>
        /// Gets the type of the main.
        /// </summary>
        public MediaType MainMediaType
        {
            get { lock (SyncLock) return m_MainMediaType; }
        }

        /// <summary>
        /// Gets the main media component of the stream to which time is synchronized.
        /// By order of priority, first Audio, then Video
        /// </summary>
        public MediaComponent Main
        {
            get { lock (SyncLock) return m_Main; }
        }

        /// <summary>
        /// Gets the video component.
        /// Returns null when there is no such stream component.
        /// </summary>
        public VideoComponent Video
        {
            get { lock (SyncLock) return m_Video; }
        }

        /// <summary>
        /// Gets the audio component.
        /// Returns null when there is no such stream component.
        /// </summary>
        public AudioComponent Audio
        {
            get { lock (SyncLock) return m_Audio; }
        }

        /// <summary>
        /// Gets the subtitles component.
        /// Returns null when there is no such stream component.
        /// </summary>
        public SubtitleComponent Subtitles
        {
            get { lock (SyncLock) return m_Subtitle; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has a video component.
        /// </summary>
        public bool HasVideo
        {
            get { lock (SyncLock) return m_Video != null; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has an audio component.
        /// </summary>
        public bool HasAudio
        {
            get { lock (SyncLock) return m_Audio != null; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has a subtitles component.
        /// </summary>
        public bool HasSubtitles
        {
            get { lock (SyncLock) return m_Subtitle != null; }
        }

        /// <summary>
        /// Gets the current length in bytes of the packet buffer for all components.
        /// These packets are the ones that have not been yet decoded.
        /// </summary>
        public long BufferLength
        {
            get { lock (SyncLock) return All.Sum(c => c.BufferLength); }
        }

        /// <summary>
        /// Gets the packet buffer length maximum.
        /// Port of ffplay.c (MAX_QUEUE_SIZE)
        /// </summary>
        public long BufferLengthThreshold => 16 * 1024 * 1024;

        /// <summary>
        /// Gets the packet buffer length progress percent, from 0.0 to 1.0.
        /// </summary>
        public double BufferLengthProgress => Math.Min((double)BufferLength / BufferLengthThreshold, 1);

        /// <summary>
        /// Gets the total number of packets in the packet buffer for all components.
        /// </summary>
        public int BufferCount
        {
            get { lock (SyncLock) return All.Sum(c => c.BufferCount); }
        }

        /// <summary>
        /// Gets the minimum number of packets to read before <see cref="HasEnoughPackets"/> returns true.
        /// </summary>
        public int BufferCountThreshold
        {
            get { lock (SyncLock) return All.Sum(c => c.BufferCountThreshold); }
        }

        /// <summary>
        /// Gets the packet buffer count progress percent, from 0.0 to 1.0.
        /// </summary>
        public double BufferCountProgress
        {
            get { lock (SyncLock) return Math.Min((double)BufferCount / BufferCountThreshold, 1); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether all packet queues contain enough packets.
        /// Port of ffplay.c stream_has_enough_packets
        /// </summary>
        public bool HasEnoughPackets
        {
            get { lock (SyncLock) return All.Any(c => c.HasEnoughPackets == false) == false; }
        }

        /// <summary>
        /// Gets or sets the <see cref="MediaComponent"/> with the specified media type.
        /// Setting a new component on an existing media type component will throw.
        /// Getting a non existing media component fro the given media type will return null.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        /// <returns>The media component</returns>
        /// <exception cref="ArgumentException">When the media type is invalid</exception>
        /// <exception cref="ArgumentNullException">MediaComponent</exception>
        public MediaComponent this[MediaType mediaType]
        {
            get
            {
                lock (SyncLock)
                {
                    switch (mediaType)
                    {
                        case MediaType.Audio: return m_Audio;
                        case MediaType.Video: return m_Video;
                        case MediaType.Subtitle: return m_Subtitle;
                        default: return null;
                    }
                }
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose() => Dispose(true);

        #endregion

        #region Methods

        /// <summary>
        /// Sends the specified packet to the correct component by reading the stream index
        /// of the packet that is being sent. No packet is sent if the provided packet is set to null.
        /// Returns the media type of the component that accepted the packet.
        /// </summary>
        /// <param name="packet">The packet.</param>
        /// <returns>The media type</returns>
        public unsafe MediaType SendPacket(MediaPacket packet)
        {
            lock (SyncLock)
            {
                if (packet == null)
                    return MediaType.None;

                foreach (var component in All)
                {
                    if (component.StreamIndex == packet.StreamIndex)
                    {
                        component.SendPacket(packet);
                        OnPacketQueued?.Invoke(packet.SafePointer, component.MediaType, BufferLength, BufferCount);

                        return component.MediaType;
                    }
                }

                return MediaType.None;
            }
        }

        /// <summary>
        /// Sends an empty packet to all media components.
        /// When an EOF/EOS situation is encountered, this forces
        /// the decoders to enter drainig mode until all frames are decoded.
        /// </summary>
        public void SendEmptyPackets()
        {
            lock (SyncLock)
                foreach (var component in All)
                    component.SendEmptyPacket();
        }

        /// <summary>
        /// Clears the packet queues for all components.
        /// Additionally it flushes the codec buffered packets.
        /// This is useful after a seek operation is performed or a stream
        /// index is changed.
        /// </summary>
        /// <param name="flushBuffers">if set to <c>true</c> flush codec buffers.</param>
        public void ClearQueuedPackets(bool flushBuffers)
        {
            lock (SyncLock)
                foreach (var component in All)
                    component.ClearQueuedPackets(flushBuffers);

            OnPacketsCleared?.Invoke();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Runs quick buffering logic on a single thread.
        /// This assumes no reading, decoding, or rendering is taking place at the time of the call.
        /// </summary>
        /// <param name="m">The media core engine.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RunQuickBuffering(MediaEngine m)
        {
            // We need to perform some packet reading and decoding
            var frame = default(MediaFrame);
            var main = Main.MediaType;
            var auxs = MediaTypes.Except(main);
            var mediaTypes = MediaTypes;

            // Read and decode blocks until the main component is half full
            while (m.ShouldReadMorePackets)
            {
                // Read some packets
                m.Container.Read();

                // Decode frames and add the blocks
                foreach (var t in mediaTypes)
                {
                    frame = this[t].ReceiveNextFrame();
                    m.Blocks[t].Add(frame, m.Container);
                }

                // Check if we have at least a half a buffer on main
                if (m.Blocks[main].CapacityPercent >= 0.5)
                    break;
            }

            // Check if we have a valid range. If not, just set it what the main component is dictating
            if (m.Blocks[main].Count > 0 && m.Blocks[main].IsInRange(m.WallClock) == false)
                m.ChangePosition(m.Blocks[main].RangeStartTime);

            // Have the other components catch up
            foreach (var t in auxs)
            {
                if (m.Blocks[main].Count <= 0) break;
                if (t != MediaType.Audio && t != MediaType.Video)
                    continue;

                while (m.Blocks[t].RangeEndTime < m.Blocks[main].RangeEndTime)
                {
                    if (m.ShouldReadMorePackets == false)
                        break;

                    // Read some packets
                    m.Container.Read();

                    // Decode frames and add the blocks
                    frame = this[t].ReceiveNextFrame();
                    m.Blocks[t].Add(frame, m.Container);
                }
            }
        }

        /// <summary>
        /// Registers the component in this component set.
        /// </summary>
        /// <param name="component">The component.</param>
        /// <exception cref="ArgumentNullException">When component of the same type is already registered</exception>
        /// <exception cref="NotSupportedException">When MediaType is not supported</exception>
        /// <exception cref="ArgumentException">When the component is null</exception>
        internal void AddComponent(MediaComponent component)
        {
            lock (SyncLock)
            {
                if (component == null)
                    throw new ArgumentNullException(nameof(component));

                switch (component.MediaType)
                {
                    case MediaType.Audio:
                        if (m_Audio != null)
                            throw new ArgumentException($"A component for '{component.MediaType}' is already registered.");

                        m_Audio = component as AudioComponent;
                        break;
                    case MediaType.Video:
                        if (m_Video != null)
                            throw new ArgumentException($"A component for '{component.MediaType}' is already registered.");

                        m_Video = component as VideoComponent;
                        break;
                    case MediaType.Subtitle:
                        if (m_Subtitle != null)
                            throw new ArgumentException($"A component for '{component.MediaType}' is already registered.");

                        m_Subtitle = component as SubtitleComponent;
                        break;
                    default:
                        throw new NotSupportedException($"Unable to register component with {nameof(MediaType)} '{component.MediaType}'");
                }

                UpdateBackingFields();
            }
        }

        /// <summary>
        /// Removes the component of specified media type (if registered).
        /// It calls the dispose method of the media component too.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        internal void RemoveComponent(MediaType mediaType)
        {
            lock (SyncLock)
            {
                var component = default(MediaComponent);
                switch (mediaType)
                {
                    case MediaType.Audio:
                        component = m_Audio;
                        m_Audio = null;
                        break;
                    case MediaType.Video:
                        component = m_Video;
                        m_Video = null;
                        break;
                    case MediaType.Subtitle:
                        component = m_Subtitle;
                        m_Subtitle = null;
                        break;
                    default:
                        break;
                }

                component?.Dispose();
                UpdateBackingFields();
            }
        }

        /// <summary>
        /// Computes the main component and backing fields.
        /// </summary>
        private void UpdateBackingFields()
        {
            var allComponents = new List<MediaComponent>(4);
            var allMediaTypes = new List<MediaType>(4);

            if (m_Audio != null)
            {
                allComponents.Add(m_Audio);
                allMediaTypes.Add(MediaType.Audio);
            }

            if (m_Video != null)
            {
                allComponents.Add(m_Video);
                allMediaTypes.Add(MediaType.Video);
            }

            if (m_Subtitle != null)
            {
                allComponents.Add(m_Subtitle);
                allMediaTypes.Add(MediaType.Subtitle);
            }

            m_All = new ReadOnlyCollection<MediaComponent>(allComponents);
            m_MediaTypes = new ReadOnlyCollection<MediaType>(allMediaTypes);
            m_Count = allComponents.Count;

            // Try for the main component to be the video (if it's not stuff like audio album art, that is)
            if (m_Video != null && m_Audio != null && m_Video.StreamInfo.IsAttachedPictureDisposition == false)
            {
                m_Main = m_Video;
                m_MainMediaType = MediaType.Video;
                return;
            }

            // If it was not vide, then it has to be audio (if it has audio)
            if (m_Audio != null)
            {
                m_Main = m_Audio;
                m_MainMediaType = MediaType.Audio;
                return;
            }

            // Set it to video even if it's attached pic stuff
            if (m_Video != null)
            {
                m_Main = m_Video;
                m_MainMediaType = MediaType.Video;
                return;
            }

            // As a last resort, set the main component to be the subtitles
            if (m_Subtitle != null)
            {
                m_Main = m_Subtitle;
                m_MainMediaType = MediaType.Subtitle;
                return;
            }

            // We whould never really hit this line
            m_Main = null;
            m_MainMediaType = MediaType.None;
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        private void Dispose(bool alsoManaged)
        {
            lock (SyncLock)
            {
                if (IsDisposed || alsoManaged == false)
                    return;

                m_IsDisposed.Value = true;
                foreach (var mediaType in m_MediaTypes)
                    RemoveComponent(mediaType);
            }
        }
    }

    #endregion
}
