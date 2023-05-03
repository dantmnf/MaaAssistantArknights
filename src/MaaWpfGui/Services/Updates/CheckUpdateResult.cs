using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MaaWpfGui.Services.Updates
{
    internal struct CheckUpdateResult
    {
        public CheckUpdateStatus Status { get; set; }

        public UpdateInfo UpdateInfo { get; set; }

        public CheckUpdateResult(CheckUpdateStatus status, UpdateInfo updateInfo)
        {
            Status = status;
            UpdateInfo = updateInfo;
        }

        public CheckUpdateResult(CheckUpdateStatus status)
        {
            Status = status;
            UpdateInfo = null;
        }
    }
}
