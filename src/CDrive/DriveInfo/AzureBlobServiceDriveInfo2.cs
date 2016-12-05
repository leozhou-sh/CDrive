﻿using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CDrive
{
    public class AzureBlobServiceDriveInfo2 : AbstractDriveInfo
    {
        public CloudBlobClient Client { get; set; }
        public string Endpoint { get; set; }

        public AzureBlobServiceDriveInfo2(string url, string name)
        {
            var parts = url.Split('?');
            var endpoint = parts[0];
            var dict = ParseValues(parts[1]);
            var accountName = dict["account"];
            var accountKey = dict["key"];

            var cred = new StorageCredentials(accountName, accountKey);
            var account = new CloudStorageAccount(cred, new StorageUri(new Uri(endpoint)), null, null, null);
            var client = account.CreateCloudBlobClient();

            this.Client = client;
            this.Endpoint = endpoint;
            this.Name = name;
        }

        internal void CreateDirectory(string path)
        {
            var parts = PathResolver.SplitPath(path);


            if (parts.Count > 0)
            {
                CreateContainerIfNotExists(parts[0]);
            }
        }

        public override void NewItem(
                            string path,
                            string type,
                            object newItemValue)
        {
            if (string.Equals(type, "Directory", StringComparison.InvariantCultureIgnoreCase))
            {
                this.CreateDirectory(path);
            }
            else if (string.Equals(type, "PageBlob", StringComparison.InvariantCultureIgnoreCase))
            {
                if (newItemValue != null)
                {
                    var size = 0L;
                    if (long.TryParse(newItemValue.ToString(), out size))
                    {
                        this.CreateEmptyFile(path, size);
                    }
                    else
                    {
                        this.CreateEmptyFile(path, 0);
                    }
                }
            }
            else if (string.Equals(type, "BlockBlob", StringComparison.InvariantCultureIgnoreCase))
            {
                var parts = PathResolver.SplitPath(path);
                if (parts.Count == 1)
                {
                    this.CreateContainerIfNotExists(parts[0]);
                }
                else
                {
                    this.CreateBlockBlob(path, newItemValue.ToString());
                }
            }
            else if (string.Equals(type, "AppendBlob", StringComparison.InvariantCultureIgnoreCase))
            {
                var parts = PathResolver.SplitPath(path);
                if (parts.Count == 1)
                {
                    this.CreateContainerIfNotExists(parts[0]);
                }
                else
                {
                    this.CreateAppendBlob(path, newItemValue.ToString());
                }
            }
            else if (string.Equals(type, "RandomPages", StringComparison.InvariantCultureIgnoreCase))
            {
                //fill page blob with random data, each page data is 512Byte, and count is required
                //e.g. ni PageBlob -type RandomPages -value <count>
                if (newItemValue != null)
                {
                    var size = 0L;
                    if (long.TryParse(newItemValue.ToString(), out size))
                    {
                        this.FillDataInPageBlob(path, size);
                    }
                    else
                    {
                        this.RootProvider.WriteWarning("Value is required.");
                    }
                }

            }
            else if (string.Equals(type, "ListPages", StringComparison.InvariantCultureIgnoreCase))
            {
                //List page ranges in page blob
                this.ListPageRanges(path);
            }
            else if (string.Equals(type, "ContainerSAStoken", StringComparison.InvariantCultureIgnoreCase))
            {
                var parts = PathResolver.SplitPath(path);
                if (parts.Count > 0)
                {
                    var containerName = parts[0];
                    var container = this.Client.GetContainerReference(containerName);
                    var policyName = string.Empty;
                    var policy = CreateBlobPolicy(newItemValue as string, ref policyName);

                    if (policyName != string.Empty) //policy-based SAStoken
                    {
                        var token = container.GetSharedAccessSignature(policy, policyName);
                        this.RootProvider.WriteItemObject(token, path, false);
                    }
                    else
                    {
                        var token = container.GetSharedAccessSignature(policy);
                        this.RootProvider.WriteItemObject(token, path, false);
                    }
                }
            }
            else if (string.Equals(type, "BlobSAStoken", StringComparison.InvariantCultureIgnoreCase))
            {
                var parts = PathResolver.SplitPath(path);
                if (parts.Count > 1)
                {
                    var containerName = parts[0];
                    var container = this.Client.GetContainerReference(containerName);
                    var blob = container.GetBlobReference(PathResolver.GetSubpath(path));
                    var policyName = string.Empty;
                    var policy = CreateBlobPolicy(newItemValue as string, ref policyName);

                    if (policyName != string.Empty) //policy-based SAStoken
                    {
                        var token = blob.GetSharedAccessSignature(policy, policyName);
                        this.RootProvider.WriteItemObject(blob.StorageUri.PrimaryUri.ToString() + token, path, false);
                    }
                    else
                    {
                        var token = blob.GetSharedAccessSignature(policy);
                        this.RootProvider.WriteItemObject(blob.StorageUri.PrimaryUri.ToString() + token, path, false);
                    }
                }
            }
            else if (string.Equals(type, "Policy", StringComparison.InvariantCultureIgnoreCase))
            {
                var parts = PathResolver.SplitPath(path);
                if (parts.Count > 0)
                {
                    var containerName = parts[0];
                    var container = this.Client.GetContainerReference(containerName);
                    var policyName = parts.Last();
                    var policyPlaceHolder = string.Empty;
                    var policy = CreateBlobPolicy(newItemValue as string, ref policyPlaceHolder);

                    var permissions = container.GetPermissions();
                    if (permissions.SharedAccessPolicies.ContainsKey(policyName))
                    {
                        if (!this.RootProvider.ShouldContinue(string.Format("Should continue to update existing policy {0}?", policyName), "Policy existed"))
                        {
                            this.RootProvider.WriteWarning("Cancelled");
                            return;
                        }
                        else
                        {
                            permissions.SharedAccessPolicies[policyName] = policy;
                        }
                    }
                    else
                    {
                        permissions.SharedAccessPolicies.Add(policyName, policy);
                    }

                    this.RootProvider.WriteWarning(string.Format("Policy {0} updated or added.", policyName));
                }

                return;
            }
            else if (string.Equals(type, "ListPolicy", StringComparison.InvariantCultureIgnoreCase))
            {
                var parts = PathResolver.SplitPath(path);
                if (parts.Count > 0)
                {
                    var containerName = parts[0];
                    var container = this.Client.GetContainerReference(containerName);

                    var permissions = container.GetPermissions();
                    foreach (var policy in permissions.SharedAccessPolicies.Keys)
                    {
                        this.RootProvider.WriteWarning(string.Format("Policy {0}", policy));
                    }

                    this.RootProvider.WriteWarning(string.Format("{0} Policies listed.", permissions.SharedAccessPolicies.Keys.Count));
                }

                return;

            }
        }

        private SharedAccessBlobPolicy CreateBlobPolicy(string permissions, ref string policyName)
        {
            if (permissions == null)
            {
                throw new Exception("Value should be set. Expected: start=<days>;expiry=<days>;policy=<policy>;p=rwdl");
            }

            ///permissions: start=0;expiry=30;policy=hello;p=rwdl
            ///
            var set = permissions.Split(';');
            var policy = new SharedAccessBlobPolicy();
            foreach (var s in set)
            {
                var p = s.Split('=');
                switch (p[0].ToLowerInvariant())
                {
                    case "expiry":
                        policy.SharedAccessExpiryTime = DateTime.Now.AddDays(Convert.ToInt32(p[1]));
                        break;
                    case "start":
                        policy.SharedAccessStartTime = DateTime.Now.AddDays(Convert.ToInt32(p[1]));
                        break;
                    case "policy":
                        policyName = p[1];
                        break;
                    case "p":
                        for (var i = 0; i < p[1].Length; ++i)
                        {
                            switch (Char.ToLowerInvariant(p[1][i]))
                            {
                                case 'r':
                                    policy.Permissions |= SharedAccessBlobPermissions.Read;
                                    break;
                                case 'w':
                                    policy.Permissions |= SharedAccessBlobPermissions.Write;
                                    break;
                                case 'd':
                                    policy.Permissions |= SharedAccessBlobPermissions.Delete;
                                    break;
                                case 'l':
                                    policy.Permissions |= SharedAccessBlobPermissions.List;
                                    break;
                            }
                        }
                        break;
                    default:
                        throw new Exception("Unknown parameter: " + p[0] + ". Expected: start=<days>;expiry=<days>;policy=<policy>;p=rwdl");
                }
            }

            return policy;
        }

        private void ListPageRanges(string path)
        {
            var parts = PathResolver.SplitPath(path);
            if (parts.Count > 1)
            {
                var blob = this.Client.GetContainerReference(parts[0]).GetPageBlobReference(PathResolver.GetSubpath(path));
                if (!blob.Exists())
                {
                    this.RootProvider.WriteWarning("PageBlob " + path + " does not exist.");
                    return;
                }

                blob.FetchAttributes();
                var totalLength = blob.Properties.Length;

                var count = 0L;
                var offset = 0L;
                var length = 4 * 1024 * 1024L; //4MB
                while (true)
                {
                    PageRange page = null;
                    var round = 0L;

                    length = (offset + length > totalLength) ? (totalLength - offset) : length;
                    foreach (var r in blob.GetPageRanges(offset, length))
                    {
                        page = r;
                        round++;
                        this.RootProvider.WriteWarning(string.Format("[{3}]\t[{0} - {1}] {2}", r.StartOffset, r.EndOffset, r.EndOffset - r.StartOffset + 1, count++));
                    }

                    if (offset + length >= totalLength)
                    {
                        //reach the end
                        break;
                    }

                    //1. move offset
                    offset += length;

                    //2. calculate next length
                    if (round < 200)
                    {
                        length *= 2;
                    }
                    else if (round > 500)
                    {
                        length /= 2;
                    }
                }
            }
            else
            {
                this.RootProvider.WriteWarning("Please specify the page blob path.");
            }
        }

        private void FillDataInPageBlob(string path, long count)
        {
            var parts = PathResolver.SplitPath(path);
            if (parts.Count > 1)
            {
                var blob = this.Client.GetContainerReference(parts[0]).GetPageBlobReference(PathResolver.GetSubpath(path));
                if (!blob.Exists())
                {
                    this.RootProvider.WriteWarning("PageBlob " + path + " does not exist.");
                    return;
                }

                blob.FetchAttributes();
                var total = blob.Properties.Length / 512;
                var data = new byte[512];
                var random = new Random();
                random.NextBytes(data);

                this.RootProvider.WriteWarning("Start writing pages...");
                var tasks = new Task[count];

                for (var i = 0; i < count; ++i)
                {
                    var p = (long)(random.NextDouble() * total);

                    var task = blob.WritePagesAsync(new MemoryStream(data), p * 512, null);
                    tasks[i] = task;
                }

                this.RootProvider.WriteWarning("Waiting writing pages...");
                Task.WaitAll(tasks);
                this.RootProvider.WriteWarning("Completed writing pages...");
            }
            else
            {
                this.RootProvider.WriteWarning("Please specify the page blob path.");
            }
        }



        public override void GetChildItems(string path, bool recurse)
        {
            var items = this.ListItems(path);
            foreach (var i in items)
            {
                this.RootProvider.WriteItemObject(i, path, true);
            }
        }

        public override void GetChildNames(string path, ReturnContainers returnContainers)
        {
            var r = AzureBlobPathResolver2.ResolvePath(this.Client, path);
            switch (r.PathType)
            {
                case PathType.AzureBlobRoot:
                    var containers = ListContainers();
                    foreach (var container in containers)
                    {
                        this.RootProvider.WriteItemObject(container, path, true);
                    }
                    break;
                default:
                    break;
            }
        }

        public override void RemoveItem(string path, bool recurse)
        {
            var r = AzureBlobPathResolver2.ResolvePath(this.Client, path);
            switch (r.PathType)
            {
                case PathType.Container:
                    r.Container.DeleteIfExists();
                    break;
                case PathType.AzureBlobQuery:
                    var files = ListItems(path) as IEnumerable<IListBlobItem>;
                    foreach(var file in files)
                    {
                        var blob = new CloudBlob(file.Uri, this.Client.Credentials);
                        blob.Delete();
                    }

                    break;
                default:
                    break;
            }
        }

        internal IEnumerable<object> ListItems(string path)
        {
            var result = AzureBlobPathResolver2.ResolvePath(this.Client, path);

            switch (result.PathType)
            {
                case PathType.AzureBlobRoot:
                    return ListContainers();
                case PathType.Container:
                case PathType.AzureBlobQuery:
                    return ListFiles(result.Container, result.BlobQuery);
                default:
                    return null;
            }
        }

        private IEnumerable<IListBlobItem> ListFiles(CloudBlobContainer container, BlobQuery blobQuery)
        {
            if (blobQuery.MaxResult == -1)
            {
                return container.ListBlobs(blobQuery.Prefix, !blobQuery.ShowDirectory, blobQuery.BlobListingDetails);
            }
            else
            {
                return ListFilesWithMaxResults(container, blobQuery);
            }
        }

        private IEnumerable<IListBlobItem> ListFilesWithMaxResults(CloudBlobContainer container, BlobQuery blobQuery)
        {
            BlobContinuationToken token = null;
            var remaining = blobQuery.MaxResult;
            while (true)
            {
                var seg = container.ListBlobsSegmented(blobQuery.Prefix, !blobQuery.ShowDirectory, blobQuery.BlobListingDetails, remaining, token, null, null);
                foreach (var i in seg.Results)
                {
                    remaining--;
                    yield return i;

                    if (remaining == 0)
                    {
                        yield break;
                    }
                }

                if (seg.ContinuationToken != null)
                {
                    token = seg.ContinuationToken;
                }
                else
                {
                    yield break;
                }
            }
        }

        private IEnumerable<CloudBlobContainer> ListContainers()
        {
            return this.Client.ListContainers();
        }

        internal CloudBlobContainer CreateContainerIfNotExists(string name)
        {
            var container = this.Client.GetContainerReference(name);
            if (!container.Exists())
            {
                container.Create();
            }

            return container;
        }

        internal void CreateEmptyFile(string path, long size)
        {
            var file = GetPageBlob(path);
            if (file == null)
            {
                throw new Exception("Path " + path + " is not a valid file path.");
            }

            file.Create(size);
        }

        private CloudPageBlob GetPageBlob(string path)
        {
            var r = AzureBlobPathResolver2.ResolvePath(this.Client, path);
            return r.Container.GetPageBlobReference(r.BlobQuery.Prefix);
        }

        internal void CreateBlockBlob(string path, string content)
        {
            var file = GetBlockBlob(path);
            if (file == null)
            {
                throw new Exception("Path " + path + " is not a valid file path.");
            }

            CreateContainerIfNotExists(file.Container.Name);
            file.UploadText(content);
        }

        private CloudBlockBlob GetBlockBlob(string path)
        {
            var r = AzureBlobPathResolver2.ResolvePath(this.Client, path);
            return r.Container.GetBlockBlobReference(r.BlobQuery.Prefix);
        }

        internal void CreateAppendBlob(string path, string content)
        {
            var file = GetAppendBlob(path);
            if (file == null)
            {
                throw new Exception("Path " + path + " is not a valid file path.");
            }

            CreateContainerIfNotExists(file.Container.Name);
            if (file.Exists())
            {
                file.AppendText(content);
            }
            else
            {
                file.UploadText(content);
            }
        }

        private CloudAppendBlob GetAppendBlob(string path)
        {
            var r = AzureBlobPathResolver2.ResolvePath(this.Client, path);
            return r.Container.GetAppendBlobReference(r.BlobQuery.Prefix);
        }

        public override IContentReader GetContentReader(string path)
        {
            var r = AzureBlobPathResolver2.ResolvePath(this.Client, path);
            var blob = r.Container.GetBlobReference(r.BlobQuery.Prefix);
            if (r.PathType == PathType.AzureBlobQuery)
            {
                var firstFile = this.ListFiles(r.Container, r.BlobQuery).FirstOrDefault();
                if (firstFile != default(IListBlobItem))
                {
                    var reader = new AzureBlobReader(new CloudBlob(firstFile.Uri, this.Client.Credentials));
                    return reader;
                }
            }

            return null;
        }


        public override bool HasChildItems(string path)
        {
            return true;
        }

        public override bool IsValidPath(string path)
        {
            throw new NotImplementedException();
        }

        public override bool ItemExists(string path)
        {
            return true;
        }

        public override bool IsItemContainer(string path)
        {
            return true;
        }

        public override void GetProperty(string path, System.Collections.ObjectModel.Collection<string> providerSpecificPickList)
        {
            
        }

        public override void SetProperty(string path, PSObject propertyValue)
        {
            
        }

        private void MergeProperties(IDictionary<string, string> target, PSMemberInfoCollection<PSPropertyInfo> source)
        {
            foreach (var info in source)
            {
                var name = info.Name;
                if (target.ContainsKey(name))
                {
                    target.Remove(name);
                }

                target.Add(name, info.Value.ToString());
            }
        }

        public override Stream CopyFrom(string path)
        {
            var r = AzureBlobPathResolver2.ResolvePath(this.Client, path);
            var files = this.ListFiles(r.Container, r.BlobQuery);
            if (files.Count() > 0)
            {
                var blob = new CloudBlob(files.First().Uri, this.Client.Credentials);
                return blob.OpenRead();
            }

            return null;
        }

        public override Stream CopyTo(string path, string name)
        {
            var r = AzureBlobPathResolver2.ResolvePath(this.Client, path + PathResolver.DirSeparator + name);
            if (r.PathType == PathType.AzureBlobQuery)
            {
                var prefix = r.BlobQuery.Prefix;
                var blob = new CloudBlockBlob(new Uri(r.Container.Uri.ToString() + "/" + prefix), this.Client.Credentials);
                return blob.OpenWrite();
            }

            return null;
        }

        public override IList<string> GetChildNamesList(string path, PathType type)
        {
            throw new NotImplementedException();
        }
    }
}
