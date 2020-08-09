using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace smoodi.updaterLib
{
    class Util
    {
        internal static string getRespectiveFilePath(XmlNode node, string installDir)
        {
            XmlNode parent = node.ParentNode;
            if (parent == null) throw new Exception("Something is odd here. Malformed updater file.");
            else if (parent.Name != "directory" && parent.Name != "program") throw new Exception("Malformed updater file. File node cannot be part of " + parent.Name);
            else
            {
                if (parent.Name == "program") return installDir + "\\" + node.Attributes[0].Value;
                else return getRespectiveFilePath(node.ParentNode, installDir) + "\\" + node.Attributes[0].Value;
            }
        }

        internal static XmlNode getDirectoryNode(string directoryName, string fullDirectoryPath, ref XmlNodeList directoryList, string installDir)
        {
            foreach (XmlNode dir in directoryList)
            {
                if(directoryName.Equals(dir.Attributes[0].Value))
                {
                    string expected = getRespectiveFilePath(dir, installDir);

                    if (fullDirectoryPath.Equals(expected))
                        return dir;
                }
            }
            return null;
        }
    }
}
