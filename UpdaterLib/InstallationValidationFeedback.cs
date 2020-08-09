using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpdaterLib
{
    public enum INSTALLATION_FILE_WARNING_TYPE{
        WAS_MISSING,
        WAS_MODIFIED,
        WAS_NOT_ALLOWED
    }
    public struct InstallationValidationFeedback
    {
        public INSTALLATION_FILE_WARNING_TYPE type { get; internal set; }
        public string file { get; internal set; }
        public InstallationValidationFeedback(INSTALLATION_FILE_WARNING_TYPE type, string file)
        {
            this.type = type;
            this.file = file;
        }
    }
}
