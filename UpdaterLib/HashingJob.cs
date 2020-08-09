using System.Xml;

namespace smoodi.updaterLib
{
    public struct HashingJob
    {
        public XmlNode node { get; }
        public string destinationFile { get; }
        public HashingJob(XmlNode node, string dest)
        {
            this.node = node;
            this.destinationFile = dest;
        }
    }
}