using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using UpdaterLib;

namespace smoodi.updaterLib
{
    public struct UpdateFile
    {
        public bool isModifyable { get; }
        public string requestSource { get; }
        public bool forceUpdate { get; }
        public string relativePath { get; }
        public string checksum { get; }

        public UpdateFile(bool modifyable, string source, bool forceUpdate, string relativePath, string checksum)
        {
            this.isModifyable = modifyable;
            this.requestSource = source;
            this.forceUpdate = forceUpdate;
            this.relativePath = relativePath;
            this.checksum = checksum;
        }
    }

    public class UpdaterLib
    {
        private DateTime lastUpdateTime;
        private float currentVersion;
        private WebClient _w = new WebClient();
        internal bool shutdownRequest = false;
        private Logger myLogger;

        public string referenceDirectory { get; set; }

        private Task xmlDownload;
        internal XmlDocument doc;

        internal ConcurrentQueue<DownloadJob> jobs = new ConcurrentQueue<DownloadJob>();

        private ISet<string> requestedSet = new HashSet<string>();
        private ISet<string> namedFiles = new HashSet<string>();

        private static ConcurrentQueue<HashingJob> hashingJobs = new ConcurrentQueue<HashingJob>();

        private int simulataniousDownloads = 1;
        public int requestedFiles = 0;
        internal int downloadedFiles = 0;
        private bool xmlLoaded = false;


        /// <summary>
        /// Initialises the updater library.
        /// </summary>
        /// <param name="patcherReference">A reference to the online update xml file</param>
        /// <param name="installDir">The local installation directory</param>
        /// <param name="simulataniousDownloads">The amount of simultanious cores and downloads to be used for faster downloading</param>
        public void init(string patcherReference, string installDir, DateTime lastUpdated, float installedVersion, int simulataniousDownloads = 1, bool logToConsole = false, bool logToFile = true)
        {
            this.lastUpdateTime = lastUpdated;
            this.currentVersion = installedVersion;

            if (installDir.EndsWith("\\") || installDir.EndsWith("/")) installDir = installDir.Substring(0, installDir.Length - 1);
            if (!Directory.Exists(installDir)) Directory.CreateDirectory(installDir);

            myLogger = new Logger(new DirectoryInfo(installDir).Parent.FullName + @"\logs", "ProjectSDownloader", logToConsole, logToFile);
            referenceDirectory = installDir;
            if (File.Exists(referenceDirectory + @"\updater_latest.log")) File.Delete(referenceDirectory + @"\updater_latest.log");
            this.simulataniousDownloads = simulataniousDownloads;

            Task<string> first = _w.DownloadStringTaskAsync(new Uri(patcherReference));
            xmlDownload = first.ContinueWith((text) => {
                string xmlraw = text.Result;
                this.doc = new XmlDocument();
                this.doc.LoadXml(xmlraw);
                xmlLoaded = true;
            });
        }

        /// <summary>
        /// Checks if the current version of the installed software is up to date.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> isUpToDate()
        {
            if (xmlLoaded)
            {
                DateTime updateReleaseTime = XmlConvert.ToDateTime(doc.SelectSingleNode("/updaterInfo/meta/updateTime").InnerText, XmlDateTimeSerializationMode.Unspecified);
                float newVersion = XmlConvert.ToSingle(doc.SelectSingleNode("/updaterInfo/meta/updateVersion").InnerText);
                int c = DateTime.Compare(lastUpdateTime, updateReleaseTime);
                return (c < 0 || (currentVersion < newVersion)) ? false : true;
            }
            else { await xmlDownload;
                DateTime updateReleaseTime = XmlConvert.ToDateTime(doc.SelectSingleNode("/updaterInfo/meta/updateTime").InnerText, XmlDateTimeSerializationMode.Unspecified);
                float newVersion = XmlConvert.ToSingle(doc.SelectSingleNode("/updaterInfo/meta/updateVersion").InnerText);
                int c = DateTime.Compare(lastUpdateTime, updateReleaseTime);
                return (c < 0 || (currentVersion < newVersion)) ? false : true;
            };
        }

        public async Task<float> getNewPatchVersion()
        {
            if(xmlLoaded) return XmlConvert.ToSingle(doc.SelectSingleNode("/updaterInfo/meta/updateVersion").InnerText);
            else { await xmlDownload; return XmlConvert.ToSingle(doc.SelectSingleNode("/updaterInfo/meta/updateVersion").InnerText); }
        }

        /// <summary>
        /// Marks the given file as a requsted one. It will only be downloaded if it can be found as a reference in the update.xml though.
        /// </summary>
        /// <param name="destination"></param>
        public void markOptionalFileAsRequested(string destination)
        {
            requestedSet.Add(destination);
        }

        /// <summary>
        /// This method will validate the current installation and mark all missing files as download required accordingly.
        /// The files that were deemed modified will be handled with given settings.
        /// The progress will be reported as a float from 0f-100f;
        /// </summary>
        /// <returns></returns>
        public async Task<IList<InstallationValidationFeedback>> validateInstallation(IProgress<float> progress)
        {
            bool redownload = XmlConvert.ToBoolean(doc.DocumentElement.SelectSingleNode("meta/forceRedownload").InnerText);
            List<InstallationValidationFeedback> list = new List<InstallationValidationFeedback>();

            #region We check for all files mentioned and enqueue them for download if missing.
            XmlNodeList files = doc.DocumentElement.SelectNodes("//file");
            string[] dirs = Directory.GetDirectories(referenceDirectory);
            myLogger.log("Checking for potentially missing files...");
            foreach (XmlNode node in files)
            {
                string name = node.Attributes[0].Value;
                bool required = XmlConvert.ToBoolean(node.Attributes[1].Value);
                string dest = Util.getRespectiveFilePath(node, referenceDirectory);
                bool requested = requestedSet.Contains(dest);
                namedFiles.Add(dest);
                if (File.Exists(dest))
                {
                    //File already exists. Let's check it's checksum
                    if (XmlConvert.ToBoolean(node.ChildNodes[1].Attributes[0].Value))
                    {
                        //This is required.
                        hashingJobs.Enqueue(new HashingJob(node, dest));
                    }
                }
                else
                {
                    if (required || requested)
                    {
                        list.Add(new InstallationValidationFeedback(INSTALLATION_FILE_WARNING_TYPE.WAS_MISSING, dest));
                        jobs.Enqueue(new DownloadJob(node.ChildNodes[0].InnerText, dest));
                        myLogger.log("Enqueuing file request for " + dest);
                    }
                }
            }
            #endregion
            myLogger.log("All necessary files have been enqueued.");

            Task task = null;

            #region We generate hashes for all existing files and then check if they need to be redownloaded as they might be modified. We also take care of other files in directories.
            if (XmlConvert.ToBoolean(doc.DocumentElement.SelectSingleNode("meta/onlyAllowListedFiles").InnerText))
            {
                myLogger.log("Checking for modified files...");
                int totalJobs = hashingJobs.Count;
                int doneJobs = 0;
                task = Task.Factory.StartNew(() => {
                    using (var md5 = MD5.Create())
                    {
                        HashingJob hj;
                        while (hashingJobs.TryDequeue(out hj))
                        {
                            byte[] hash;
                            using (var stream = File.OpenRead(hj.destinationFile))
                            {
                                hash = md5.ComputeHash(stream);
                            }
                            if (BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Equals(hj.node.ChildNodes[1].InnerText))
                            {
                                //Hash correct
                            }
                            else
                            {
                                handleModifiedFiles(hj.destinationFile, hj.node.ChildNodes[0].InnerText, list);
                            }

                            doneJobs++;
                            myLogger.log("Validating installation: " + ((float)doneJobs / (float)totalJobs * 100f) + " % completed.");
                            progress.Report((float)doneJobs / (float)totalJobs * 100f);
                        }
                    }
                });


                bool customFolders = XmlConvert.ToBoolean(doc.DocumentElement.SelectSingleNode("meta/allowCustomDirectories").InnerText);
                XmlNodeList directories = doc.DocumentElement.SelectNodes("//directory");
                foreach (string f in Directory.GetFiles(referenceDirectory, "*", SearchOption.AllDirectories))
                {
                    if (namedFiles.Contains(f)) { continue; }// This file is intended;
                    else
                    {
                        FileInfo fI = new FileInfo(f);
                        XmlNode dir = Util.getDirectoryNode(fI.Directory.Name, fI.Directory.FullName, ref directories, referenceDirectory);
                        if (dir == null)
                        {
                            if (customFolders) continue;
                            else handleNotAllowedFiles(f, list);
                        }
                        else
                        {
                            if (XmlConvert.ToBoolean(dir.Attributes[1].Value)) continue;
                            else handleNotAllowedFiles(f, list);
                        }
                    }
                }
                myLogger.log("File checks completed.");
            }
            #endregion
            if (task != null) await task;
            else myLogger.log("Free modifications allowed. No file check performed.", Logger.LOGTYPE.WARNING);
            return list;
        }

        /// <summary>
        /// Returns the amount of files (left) for download.
        /// </summary>
        /// <returns></returns>
        public int getPendingDownloads()
        {
          return jobs.Count;
        }

        /// <summary>
        /// Sends a shutdown request to all downloader tasks.
        /// </summary>
        public void requestShutdown()
        {
            this.shutdownRequest = true;
        }

        /// <summary>
        /// Downloads all pending files.
        /// </summary>
        /// <param name="progressReport">A progress instance used to report the already downloaded amount of files. Use getPendingDownloads before starting to get a percentage.</param>
        /// <returns>The amount of failed downloads.</returns>
        public async Task<int> downloadPendingFiles(IProgress<int> progressReport)
        {
            requestedFiles = jobs.Count;
            int failedOperations = 0;

            myLogger.log("Downloading " + requestedFiles + " requested files...");
            myLogger.log("Initialising " + simulataniousDownloads + " simulatanious downloader instances...");
            Task[] downloaderThreads = new Task[simulataniousDownloads];
            for (int i = 0; i < simulataniousDownloads; i++) {
                downloaderThreads[i] = Task.Factory.StartNew(() =>
                {
                    WebClient w = new WebClient();
                    DownloadJob job;
                    while (jobs.TryDequeue(out job) && !shutdownRequest)
                    {
                        try {
                            if (File.Exists(job.destination)) File.Delete(job.destination);
                            if (!Directory.Exists(new FileInfo(job.destination).Directory.FullName)) Directory.CreateDirectory(new FileInfo(job.destination).Directory.FullName);
                            myLogger.log("Downloading " + job.destination + " from " + job.url);
                            w.DownloadFile(job.url, job.destination);
                        }
                        catch (Exception e)
                        {
                            myLogger.log("Download of " + job.destination + " from " + job.url + " failed. Further info: " + e.Message + ", Stacktrace: " + e.StackTrace+"!", Logger.LOGTYPE.ERROR);
                            Interlocked.Increment(ref failedOperations);
                        }
                        Interlocked.Increment(ref downloadedFiles);
                        progressReport.Report(downloadedFiles);
                        }
                });
            }
            for (int i = 0; i < simulataniousDownloads; i++) { await downloaderThreads[i]; }
            if (!shutdownRequest) myLogger.log("Downloads finished.");
            else myLogger.log("Downloads have been shut down early.");

            return failedOperations;
        }


        private void handleModifiedFiles(string f, string url, List<InstallationValidationFeedback> list)
        {
            list.Add(new InstallationValidationFeedback(INSTALLATION_FILE_WARNING_TYPE.WAS_MODIFIED, f));
            myLogger.log("File " + f + " seems to have been modified. Rerequesting file.", Logger.LOGTYPE.WARNING);
            File.Delete(f);
            jobs.Enqueue(new DownloadJob(url, f));
        }

        private void handleNotAllowedFiles(string f, List<InstallationValidationFeedback> list)
        {
            list.Add(new InstallationValidationFeedback(INSTALLATION_FILE_WARNING_TYPE.WAS_NOT_ALLOWED, f));
            myLogger.log("File " + f + " is not allowed. Deleting file.", Logger.LOGTYPE.WARNING);
            File.Delete(f);
        }

        public void readLocal(string file)
        {
            doc = new XmlDocument();
            doc.Load(file);
        }
    }
}

