using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MaaWpfGui.Services.Updates
{
    public class UpdateInfo
    {
        public string Name { get; set; }

        public string Tag { get; set; }

        public string ReleaseNotes { get; set; }

        public string ReleaseWebPageUrl { get; set; }

        public JObject GitHubReleaseAsset { get; set; }

        public string AssetName { get; set; }
    }
}
