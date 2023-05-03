using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MaaWpfGui.Services.Updates
{
    public enum UpdateStatus
    {
        None,
        Available,
        Downloading,
        Downloaded,
        DownloadError,
    }
}
