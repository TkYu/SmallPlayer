using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WPFSpark;

namespace SmallPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly MainWindowViewModel vm = MainWindowViewModel.Instance;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = vm;
            vm.PropertyChanged += Vm_PropertyChanged; ;
            vm.MainDispatcher = Application.Current.Dispatcher;
            spectrumAnalyzer.RegisterSoundPlayer(vm);
            waveformTimeline.RegisterSoundPlayer(vm);
        }

        private void Vm_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "FileTag":
                    if (vm.FileTag != null)
                    {
                        TagLib.Tag tag = vm.FileTag.Tag;
                        if (tag.Pictures.Length > 0)
                        {
                            using (MemoryStream albumArtworkMemStream = new MemoryStream(tag.Pictures[0].Data.Data))
                            {
                                try
                                {
                                    BitmapImage albumImage = new BitmapImage();
                                    albumImage.BeginInit();
                                    albumImage.CacheOption = BitmapCacheOption.OnLoad;
                                    albumImage.StreamSource = albumArtworkMemStream;
                                    albumImage.EndInit();
                                    albumArtPanel.AlbumArtImage = albumImage;
                                }
                                catch (NotSupportedException)
                                {
                                    albumArtPanel.AlbumArtImage = null;
                                    // System.NotSupportedException:
                                    // No imaging component suitable to complete this operation was found.
                                }
                                albumArtworkMemStream.Close();
                            }
                        }
                        else
                        {
                            albumArtPanel.AlbumArtImage = null;
                        }
                    }
                    else
                    {
                        albumArtPanel.AlbumArtImage = null;
                    }
                    break;
                case "ChannelPosition":
                    //clockDisplay.Time = TimeSpan.FromSeconds(engine.ChannelPosition);
                    break;
                default:
                    // Do Nothing
                    break;
            }
        }
    }
}
