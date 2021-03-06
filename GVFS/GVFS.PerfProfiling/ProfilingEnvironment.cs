﻿using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.GVFlt;

namespace GVFS.PerfProfiling
{
    class ProfilingEnvironment
    {
        public ProfilingEnvironment(string enlistmentRootPath)
        {
            this.Enlistment = this.CreateEnlistment(enlistmentRootPath);
            this.Context = this.CreateContext();
            this.GVFltCallbacks = this.CreateGVFltCallbacks();
        }

        public GVFSEnlistment Enlistment { get; private set; }
        public GVFSContext Context { get; private set; }
        public GVFltCallbacks GVFltCallbacks { get; private set; }

        private GVFSEnlistment CreateEnlistment(string enlistmentRootPath)
        {
            string gitBinPath = GitProcess.GetInstalledGitBinPath();
            string hooksPath = ProcessHelper.WhereDirectory(GVFSConstants.GVFSHooksExecutableName);

            return GVFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, hooksPath);
        }

        private GVFSContext CreateContext()
        {
            ITracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "GVFS.PerfProfiling", useCriticalTelemetryFlag: false);

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo gitRepo = new GitRepo(
                tracer, 
                this.Enlistment, 
                fileSystem);
            return new GVFSContext(tracer, fileSystem, gitRepo, this.Enlistment);
        }

        private GVFltCallbacks CreateGVFltCallbacks()
        {
            string error;
            if (!RepoMetadata.TryInitialize(this.Context.Tracer, this.Enlistment.DotGVFSRoot, out error))
            {
                throw new InvalidRepoException(error);
            }

            string gitObjectsRoot;
            if (!RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error))
            {
                throw new InvalidRepoException("Failed to determine git objects root from repo metadata: " + error);
            }

            string localCacheRoot;
            if (!RepoMetadata.Instance.TryGetLocalCacheRoot(out localCacheRoot, out error))
            {
                throw new InvalidRepoException("Failed to determine local cache path from repo metadata: " + error);
            }

            string blobSizesRoot;
            if (!RepoMetadata.Instance.TryGetBlobSizesRoot(out blobSizesRoot, out error))
            {
                throw new InvalidRepoException("Failed to determine blob sizes root from repo metadata: " + error);
            }

            this.Enlistment.InitializeCachePaths(localCacheRoot, gitObjectsRoot, blobSizesRoot);

            CacheServerInfo cacheServer = new CacheServerInfo(this.Context.Enlistment.RepoUrl, "None");
            GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(
                this.Context.Tracer, 
                this.Context.Enlistment,
                cacheServer,
                new RetryConfig());

            GVFSGitObjects gitObjects = new GVFSGitObjects(this.Context, objectRequestor);
            return new GVFltCallbacks(this.Context, gitObjects, RepoMetadata.Instance);
        }
    }
}
