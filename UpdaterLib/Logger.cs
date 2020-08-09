using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace smoodi.updaterLib
{
    class Logger
    {
        public enum LOGTYPE
        {
            INFO,
            WARNING,
            SEVERE,
            ERROR
        }

        public static bool preserveLogs = true;
        private string file;
        private bool useConsole = false;
        private bool useFile = false;
        private bool writing = false;
        private ConcurrentQueue<string> pendingText = new ConcurrentQueue<string>();

        /// <summary>
        /// Creates a new logger instance.
        /// </summary>
        /// <param name="loggingDir">The directory for logs to be created in.</param>
        /// <param name="loggerName">The logger program name. In case multiple programs log in the same folder. Can be null for no custom name.</param>
        public Logger(string loggingDir, string loggerName = null, bool useConsole = true, bool useFile = true)
        {
            this.useConsole = useConsole;
            this.useFile = useFile;

            if (!useFile) return;
            if (!Directory.Exists(loggingDir)) Directory.CreateDirectory(loggingDir);
            this.file = loggingDir + @"\updater" + ((loggerName != null)  ? "_" + loggerName : "") + "_latest.log";
            if (File.Exists(file))
            {
                if (preserveLogs)
                {
                    DateTime oldLogTime = File.GetCreationTime(file);
                    string newMove = loggingDir + @"\updater" + ((loggerName != null) ? "_" + loggerName : "") + "_" + DateTime.Now.ToString("yyyy-MM-dd-THH-_mm_ss") + ".log";
                    Console.WriteLine("New move: " + newMove);
                    if (!File.Exists(newMove))
                        File.Move(file, newMove);
                    else
                        File.Delete(file);
                }
                else
                {
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// Logs a given text and prints it to the console as well as logging it in a file.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="type"></param>
        public void log(string msg, LOGTYPE type = LOGTYPE.INFO) {
            if(useFile)pendingText.Enqueue("[" + DateTime.Now.ToString() + " / " + type.ToString() + "] " + msg);
            if(useConsole) Console.WriteLine("[" + DateTime.Now.ToString() + " / " + type.ToString() + "] " + msg);
            if(useFile)writeToLog();
        }

        private async void writeToLog()
        {
            if (writing) return;
            writing = true;
            if (!File.Exists(file))
            {
            // Create a file to write to.
                using (StreamWriter sw = File.CreateText(file))
                {
                    string text;
                    while (pendingText.TryDequeue(out text)) {
                        await sw.WriteLineAsync(text);
                    }
                }
            }
            else
            {
                using (StreamWriter sw = File.AppendText(file))
                {
                    string text;
                    while (pendingText.TryDequeue(out text))
                    {
                        await sw.WriteLineAsync(text);
                    }
                }
            }
            writing = false;
        }
    }
}
