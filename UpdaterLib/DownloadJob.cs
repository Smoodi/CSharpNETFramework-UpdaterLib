namespace smoodi.updaterLib
{
    public struct DownloadJob
    {
        public string url { get; }
        public string destination { get; }
        public DownloadJob(string url, string destination)
        {
            this.url = url;
            this.destination = destination;
        }
    }
}