using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Un4seen.Bass;
using WPFSoundVisualizationLib;

namespace SmallPlayer
{
    public sealed class MainWindowViewModel : IWaveformPlayer, ISpectrumPlayer
    {
        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Fields
        private static MainWindowViewModel instance;
        private readonly DispatcherTimer positionTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        private readonly int fftDataSize = (int)FFTDataSize.FFT2048;
        private readonly int maxFFT = (int)(BASSData.BASS_DATA_AVAILABLE | BASSData.BASS_DATA_FFT2048);
        private readonly SYNCPROC endTrackSyncProc;
        private readonly SYNCPROC repeatSyncProc;
        private int sampleFrequency = 44100;
        private int activeStreamHandle;
        private TagLib.File fileTag;
        private bool canPlay;
        private bool canPause;
        private bool isPlaying;
        private bool canStop;
        private double channelLength;
        private double currentChannelPosition;
        private float[] fullLevelData;
        private float[] waveformData;
        private bool inChannelSet;
        private bool inChannelTimerUpdate;
        private int repeatSyncId;
        private TimeSpan repeatStart;
        private TimeSpan repeatStop;
        private bool inRepeatSet;

        private string title;
        #endregion

        #region Singleton Instance
        public Dispatcher MainDispatcher { get; set; } = null;
        public static MainWindowViewModel Instance => instance ?? (instance = new MainWindowViewModel());
        #endregion

        #region Constants
        private const int waveformCompressedPointCount = 2000;
        private const int repeatThreshold = 200;
        #endregion

        #region Constructor
        private MainWindowViewModel()
        {
            Initialize();
            endTrackSyncProc = EndTrack;
            repeatSyncProc = RepeatCallback;
        }
        #endregion

        #region ISpectrumPlayer
        public int GetFFTFrequencyIndex(int frequency)
        {
            return Utils.FFTFrequency2Index(frequency, fftDataSize, sampleFrequency);
        }

        public bool GetFFTData(float[] fftDataBuffer)
        {
            return Bass.BASS_ChannelGetData(ActiveStreamHandle, fftDataBuffer, maxFFT) > 0;
        }
        #endregion

        #region IWaveformPlayer

        

        public TimeSpan SelectionBegin
        {
            get { return repeatStart; }
            set
            {
                if (inRepeatSet) return;
                inRepeatSet = true;
                var oldValue = repeatStart;
                repeatStart = value;
                if (oldValue != repeatStart)
                    OnPropertyChanged();
                SetRepeatRange(value, SelectionEnd);
                inRepeatSet = false;
            }
        }

        public TimeSpan SelectionEnd
        {
            get { return repeatStop; }
            set
            {
                if (inChannelSet) return;
                inRepeatSet = true;
                var oldValue = repeatStop;
                repeatStop = value;
                if (oldValue != repeatStop)
                    OnPropertyChanged();
                SetRepeatRange(SelectionBegin, value);
                inRepeatSet = false;
            }
        }

        public double ChannelLength
        {
            get { return channelLength; }
            private set
            {
                double oldValue = channelLength;
                channelLength = value;
                if (!oldValue.Equals(channelLength))
                    OnPropertyChanged();
            }
        }

        public double ChannelPosition
        {
            get { return currentChannelPosition; }
            set
            {
                if (!inChannelSet)
                {
                    inChannelSet = true; // Avoid recursion
                    double oldValue = currentChannelPosition;
                    double position = Math.Max(0, Math.Min(value, ChannelLength));
                    if (!inChannelTimerUpdate)
                        Bass.BASS_ChannelSetPosition(ActiveStreamHandle, Bass.BASS_ChannelSeconds2Bytes(ActiveStreamHandle, position));
                    currentChannelPosition = position;
                    if (!oldValue.Equals(currentChannelPosition))
                        OnPropertyChanged();
                    inChannelSet = false;
                }
            }
        }

        public float[] WaveformData
        {
            get { return waveformData; }
            private set
            {
                var oldValue = waveformData;
                waveformData = value;
                if (oldValue != waveformData)
                    OnPropertyChanged();
            }
        }

        public float[] FullLevelData
        {
            get { return fullLevelData; }
            private set
            {
                var oldValue = fullLevelData;
                fullLevelData = value;
                if (oldValue != fullLevelData)
                    OnPropertyChanged();
            }
        }
        #endregion

        #region Public Methods
        public void Stop()
        {
            ChannelPosition = SelectionBegin.TotalSeconds;
            if (ActiveStreamHandle != 0)
            {
                Bass.BASS_ChannelStop(ActiveStreamHandle);
                Bass.BASS_ChannelSetPosition(ActiveStreamHandle, ChannelPosition);
            }
            IsPlaying = false;
            CanStop = false;
            CanPlay = true;
            CanPause = false;
        }

        public void Pause()
        {
            if (IsPlaying && CanPause)
            {
                Bass.BASS_ChannelPause(ActiveStreamHandle);
                IsPlaying = false;
                CanPlay = true;
                CanPause = false;
            }
        }

        public void Play()
        {
            if (CanPlay)
            {
                PlayCurrentStream();
                IsPlaying = true;
                CanPause = true;
                CanPlay = false;
                CanStop = true;
            }
        }

        public void PlayOrPause()
        {
            if(IsPlaying)
                Pause();
            else
                Play();
        }

        public bool OpenFile(string path)
        {
            Stop();
            if (CurrentFile == path)
                return true;
            CurrentFile = path;
            if (ActiveStreamHandle != 0)
            {
                ClearRepeatRange();
                ChannelPosition = 0;
                Bass.BASS_StreamFree(ActiveStreamHandle);
            }

            if (System.IO.File.Exists(path))
            {
                // Create Stream
                FileStreamHandle = ActiveStreamHandle = Bass.BASS_StreamCreateFile(path, 0, 0, BASSFlag.BASS_SAMPLE_FLOAT | BASSFlag.BASS_STREAM_PRESCAN);
                ChannelLength = Bass.BASS_ChannelBytes2Seconds(FileStreamHandle, Bass.BASS_ChannelGetLength(FileStreamHandle, 0));
                FileTag = TagLib.File.Create(path);
                GenerateWaveformData(path);
                if (ActiveStreamHandle != 0)
                {
                    // Obtain the sample rate of the stream
                    BASS_CHANNELINFO info = new BASS_CHANNELINFO();
                    Bass.BASS_ChannelGetInfo(ActiveStreamHandle, info);
                    sampleFrequency = info.freq;

                    // Set the stream to call Stop() when it ends.
                    int syncHandle = Bass.BASS_ChannelSetSync(ActiveStreamHandle,
                         BASSSync.BASS_SYNC_END,
                         0,
                         endTrackSyncProc,
                         IntPtr.Zero);

                    if (syncHandle == 0)
                        throw new ArgumentException("Error establishing End Sync on file stream.", nameof(path));

                    CanPlay = true;
                    return true;
                }
                ActiveStreamHandle = 0;
                FileTag = null;
                CanPlay = false;
            }
            return false;
        }

        public void Free()
        {
            Stop();
            Bass.BASS_Free();
        }
        #endregion

        #region Event Handleres
        private void positionTimer_Tick(object sender, EventArgs e)
        {
            if (ActiveStreamHandle == 0)
            {
                ChannelPosition = 0;
            }
            else
            {
                inChannelTimerUpdate = true;
                ChannelPosition = Bass.BASS_ChannelBytes2Seconds(ActiveStreamHandle, Bass.BASS_ChannelGetPosition(ActiveStreamHandle, 0));
                inChannelTimerUpdate = false;
            }
        }
        #endregion

        #region Waveform Generation

        private CancellationTokenSource generateWaveCancelTokenSource;
        private void GenerateWaveformData(string path,int points = waveformCompressedPointCount)
        {
            if (generateWaveCancelTokenSource != null && !generateWaveCancelTokenSource.IsCancellationRequested)
                generateWaveCancelTokenSource.Cancel();
            generateWaveCancelTokenSource = new CancellationTokenSource();

            Task.Run(() =>
            {
                int stream = Bass.BASS_StreamCreateFile(path, 0, 0, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_SAMPLE_FLOAT | BASSFlag.BASS_STREAM_PRESCAN);
                int frameLength = (int)Bass.BASS_ChannelSeconds2Bytes(stream, 0.02);
                long streamLength = Bass.BASS_ChannelGetLength(stream, 0);
                int frameCount = (int)(streamLength / (double)frameLength);
                int waveformLength = frameCount * 2;
                float[] waveform = new float[waveformLength];
                float[] levels = new float[2];

                int actualPoints = Math.Min(points, frameCount);

                int compressedPointCount = actualPoints * 2;
                float[] waveformCompressedPoints = new float[compressedPointCount];
                List<int> waveMaxPointIndexes = new List<int>();
                for (int i = 1; i <= actualPoints; i++)
                {
                    waveMaxPointIndexes.Add((int)Math.Round(waveformLength * (i / (double)actualPoints), 0));
                }

                float maxLeftPointLevel = float.MinValue;
                float maxRightPointLevel = float.MinValue;
                int currentPointIndex = 0;
                for (int i = 0; i < waveformLength; i += 2)
                {
                    Bass.BASS_ChannelGetLevel(stream, levels);
                    waveform[i] = levels[0];
                    waveform[i + 1] = levels[1];

                    if (levels[0] > maxLeftPointLevel)
                        maxLeftPointLevel = levels[0];
                    if (levels[1] > maxRightPointLevel)
                        maxRightPointLevel = levels[1];

                    if (i > waveMaxPointIndexes[currentPointIndex])
                    {
                        waveformCompressedPoints[(currentPointIndex * 2)] = maxLeftPointLevel;
                        waveformCompressedPoints[(currentPointIndex * 2) + 1] = maxRightPointLevel;
                        maxLeftPointLevel = float.MinValue;
                        maxRightPointLevel = float.MinValue;
                        currentPointIndex++;
                    }
                    if (i % 3000 == 0)
                    {
                        float[] clonedData = (float[])waveformCompressedPoints.Clone();
                        if (MainDispatcher == null)
                        {
                            WaveformData = clonedData;
                        }
                        else
                        {
                            MainDispatcher.Invoke(() => { WaveformData = clonedData; });
                        }
                    }
                    if (generateWaveCancelTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
                float[] finalClonedData = (float[])waveformCompressedPoints.Clone();
                if (MainDispatcher == null)
                {
                    FullLevelData = waveform;
                    WaveformData = finalClonedData;
                }
                else
                {
                    MainDispatcher.Invoke(() =>
                    {
                        FullLevelData = waveform;
                        WaveformData = finalClonedData;
                    });
                }
                Bass.BASS_StreamFree(stream);
            }, generateWaveCancelTokenSource.Token);
        }

        #endregion

        #region Private Utility Methods
        private void Initialize()
        {
            positionTimer.Interval = TimeSpan.FromMilliseconds(50);
            positionTimer.Tick += positionTimer_Tick;

            IsPlaying = false;

            if (Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_SPEAKERS,IntPtr.Zero))
            {
                int pluginAAC = Bass.BASS_PluginLoad("bass_aac.dll");
#if DEBUG
                BASS_INFO info = new BASS_INFO();
                Bass.BASS_GetInfo(info);
                Debug.WriteLine(info.ToString());
                BASS_PLUGININFO aacInfo = Bass.BASS_PluginGetInfo(pluginAAC);
                foreach (BASS_PLUGINFORM f in aacInfo.formats)
                    Debug.WriteLine("Type={0}, Name={1}, Exts={2}", f.ctype, f.name, f.exts);
#endif
            }
            else
            {
                MessageBox.Show("Bass initialization error!");
            }
        }

        private void SetRepeatRange(TimeSpan startTime, TimeSpan endTime)
        {
            if (repeatSyncId != 0)
                Bass.BASS_ChannelRemoveSync(ActiveStreamHandle, repeatSyncId);

            if ((endTime - startTime) > TimeSpan.FromMilliseconds(repeatThreshold))
            {
                long cLength = Bass.BASS_ChannelGetLength(ActiveStreamHandle);
                long endPosition = (long)(endTime.TotalSeconds / ChannelLength * cLength);
                repeatSyncId = Bass.BASS_ChannelSetSync(ActiveStreamHandle,
                    BASSSync.BASS_SYNC_POS,
                    endPosition,
                    repeatSyncProc,
                    IntPtr.Zero);
                ChannelPosition = SelectionBegin.TotalSeconds;
            }
            else
                ClearRepeatRange();
        }

        private void ClearRepeatRange()
        {
            if (repeatSyncId != 0)
            {
                Bass.BASS_ChannelRemoveSync(ActiveStreamHandle, repeatSyncId);
                repeatSyncId = 0;
            }
        }

        private void PlayCurrentStream()
        {
            // Play Stream
            if (ActiveStreamHandle != 0 && Bass.BASS_ChannelPlay(ActiveStreamHandle, false))
            {
                // Do nothing
            }
#if DEBUG
            else
            {
                Debug.WriteLine("Error={0}", Bass.BASS_ErrorGetCode());
            }
#endif
        }
        #endregion

        #region Callbacks
        private void EndTrack(int handle, int channel, int data, IntPtr user)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() => Stop()));
        }

        private void RepeatCallback(int handle, int channel, int data, IntPtr user)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() => ChannelPosition = SelectionBegin.TotalSeconds));
        }
        #endregion

        #region Commands
        private ICommand _stopCommand;
        public ICommand StopCommand => _stopCommand ?? (_stopCommand = new SimpleCommand(Stop));
        private ICommand _playCommand;
        public ICommand PlayCommand => _playCommand ?? (_playCommand = new SimpleCommand(PlayOrPause));
        #endregion

        #region Public Properties
        public int FileStreamHandle
        {
            get { return activeStreamHandle; }
            private set
            {
                int oldValue = activeStreamHandle;
                activeStreamHandle = value;
                if (oldValue != activeStreamHandle)
                    OnPropertyChanged();
            }
        }

        public int ActiveStreamHandle
        {
            get { return activeStreamHandle; }
            private set
            {
                int oldValue = activeStreamHandle;
                activeStreamHandle = value;
                if (oldValue != activeStreamHandle)
                    OnPropertyChanged();
            }
        }

        public TagLib.File FileTag
        {
            get { return fileTag; }
            private set
            {
                TagLib.File oldValue = fileTag;
                fileTag = value;
                if (oldValue != fileTag)
                {
                    OnPropertyChanged();
                    var tag = fileTag.Tag;
                    if (tag.Pictures.Length > 0)
                    {
                        using (var albumArtworkMemStream = new System.IO.MemoryStream(tag.Pictures[0].Data.Data))
                        {
                            try
                            {
                                var ai = new System.Windows.Media.Imaging.BitmapImage();
                                ai.BeginInit();
                                ai.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                ai.StreamSource = albumArtworkMemStream;
                                ai.EndInit();
                                AlbumImage = ai;
                            }
                            catch (NotSupportedException)
                            {
                                AlbumImage = null;
                                // System.NotSupportedException:
                                // No imaging component suitable to complete this operation was found.
                            }
                            albumArtworkMemStream.Close();
                        }
                    }
                    else
                    {
                        AlbumImage = null;
                    }
                }
            }
        }

        private System.Windows.Media.Imaging.BitmapImage albumImage;

        public System.Windows.Media.Imaging.BitmapImage AlbumImage
        {
            get { return albumImage; }
            private set
            {
                var oldValue = albumImage;
                albumImage = value;
                if (!Equals(oldValue, albumImage))
                    OnPropertyChanged();
            }
        }

        public bool CanPlay
        {
            get { return canPlay; }
            private set
            {
                bool oldValue = canPlay;
                canPlay = value;
                if (oldValue != canPlay)
                    OnPropertyChanged();
            }
        }

        public bool CanPause
        {
            get { return canPause; }
            private set
            {
                bool oldValue = canPause;
                canPause = value;
                if (oldValue != canPause)
                    OnPropertyChanged();
            }
        }

        public bool CanStop
        {
            get { return canStop; }
            private set
            {
                bool oldValue = canStop;
                canStop = value;
                if (oldValue != canStop)
                    OnPropertyChanged();
            }
        }

        public bool IsPlaying
        {
            get { return isPlaying; }
            private set
            {
                bool oldValue = isPlaying;
                isPlaying = value;
                if (oldValue != isPlaying)
                    OnPropertyChanged();
                positionTimer.IsEnabled = value;
            }
        }

        public string CurrentFile { get; private set; }

        #endregion
    }
}
