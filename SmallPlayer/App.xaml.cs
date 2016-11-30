using Microsoft.Shell;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SmallPlayer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// 
    /// See: http://blogs.microsoft.co.il/arik/2010/05/28/wpf-single-instance-application/
    /// 
    /// </summary>
    public partial class App : Application, ISingleInstanceApp
    {
        private const string UniqueName = @"TheBassNetSmallPlayer";
        private static App _application;
        
        [STAThread]
        public static void Main(string[] args)
        {
            if (SingleInstance<App>.InitializeAsFirstInstance(UniqueName))
            {
                
                if (args.Length != 0)
                {
                    var player = MainWindowViewModel.Instance;
                    player.OpenFile(args[0]);
                    player.Play();

                    _application = new App();
                    _application.InitializeComponent();
                    _application.Run();

                    player.Free();
                }
               
                
                // Allow single instance code to perform cleanup operations
                SingleInstance<App>.Cleanup();
            }
        }

        

        #region ISingleInstanceApp Members

        public bool SignalExternalCommandLineArgs(IList<string> args)
        {
            // this is executed in the original instance

            // we get the arguments to the second instance and can send them to the existing instance if desired

            // here we bring the existing instance to the front
            _application.MainWindow.BringToFront();
            var player = MainWindowViewModel.Instance;
            if (args.Count > 1)
            {
                player.OpenFile(args[1]);
                player.Play();
            }
            // handle command line arguments of second instance

            return true;
        }

        #endregion
    }
}
