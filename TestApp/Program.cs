using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp
{
    class Program
    {
        private static int downloads_count;

        static async Task Main(string[] args)
        {
            smoodi.updaterLib.UpdaterLib updaterLib = new smoodi.updaterLib.UpdaterLib();
            updaterLib.init("http://dl.smoodi.de/projects/patch/game/update.xml", @"C:\Users\smood\AppData\Roaming\SmoodiGames\games\minecraft\modpacks\projects\game\master\java", DateTime.Now, 0.1f, 1, true, true);
            bool uptoDate = await updaterLib.isUpToDate();
            Console.WriteLine("Up to date: " + uptoDate);

            Progress<float> progress = new Progress<float>();
            progress.ProgressChanged += validationProgressChanged;

            await updaterLib.validateInstallation(progress);

            Progress<int> downloads = new Progress<int>();
            downloads.ProgressChanged += DownloadsIncreased;

            downloads_count = updaterLib.getPendingDownloads();
            await updaterLib.downloadPendingFiles(downloads);

            Console.WriteLine("Tasks finished");
            Console.Read();
        }

        private static void DownloadsIncreased(object sender, int e)
        {
            Console.WriteLine("Downloading of " + e + " / " + downloads_count + " files completed.");
        }

        private static void validationProgressChanged(object sender, float e)
        {
        }
    }
}
