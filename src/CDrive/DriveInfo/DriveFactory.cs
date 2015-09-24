﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Provider;
using System.Text;
using System.Threading.Tasks;

namespace CDrive
{
    public static class DriveFactory
    {

        public static AbstractDriveInfo CreateInstance(string type, object value, string name)
        {
            switch (type.ToLowerInvariant())
            {
                case "azurefile":
                    var d = new AzureFileServiceDriveInfo(value as string, name);
                    return d;
                case "azureblob":
                    var b = new AzureBlobServiceDriveInfo(value as string, name);
                    return b;
                case "azuretable":
                    var t = new AzureTableServiceDriveInfo(value as string, name);
                    return t;
                case "azurequeue":
                    var q = new AzureQueueServiceDriveInfo(value as string, name);
                    return q; 
                default:
                    return null;
                    
            }
        }
    }
}
