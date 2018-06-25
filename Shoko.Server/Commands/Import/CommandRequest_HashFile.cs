﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Xml;
using NLog;
using NutzCode.CloudFileSystem;
using Shoko.Commons.Queue;
using Shoko.Models.Azure;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.FileHelper;
using Shoko.Server.Models;
using Shoko.Server.Providers.Azure;
using Shoko.Server.Repositories;
using Shoko.Server.Repositories.Cached;
using File = Pri.LongPath.File;
using Path = Pri.LongPath.Path;

namespace Shoko.Server.Commands
{
    [Serializable]
    [Command(CommandRequestType.HashFile)]
    public class CommandRequest_HashFile : CommandRequestImplementation
    {
        public virtual string FileName { get; set; }
        public virtual bool ForceHash { get; set; }

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority4;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            queueState = QueueStateEnum.CheckingFile,
            extraParams = new[] {FileName}
        };

        public QueueStateStruct PrettyDescriptionHashing => new QueueStateStruct
        {
            queueState = QueueStateEnum.HashingFile,
            extraParams = new[] {FileName}
        };

        public CommandRequest_HashFile()
        {
        }

        public CommandRequest_HashFile(string filename, bool force)
        {
            FileName = filename;
            ForceHash = force;
            Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Trace("Checking File For Hashes: {0}", FileName);

            try
            {
                ProcessFile_LocalInfo();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error processing file: {0}\n{1}", FileName, ex);
            }
        }

        //Added size return, since symbolic links return 0, we use this function also to return the size of the file.
        private long CanAccessFile(string fileName)
        {
            try
            {
                using (FileStream fs = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    long size = fs.Seek(0, SeekOrigin.End);
                    fs.Close();
                    return size;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                return 0;
            }
        }

        private void ProcessFile_LocalInfo()
        {
            // hash and read media info for file
            int nshareID = -1;


            (SVR_ImportFolder, string) tup = VideoLocal_PlaceRepository.GetFromFullPath(FileName);
            if (tup.Item1 == null)
            {
                logger.Error($"Unable to locate Import Folder for {FileName}");
                return;
            }
            SVR_ImportFolder folder = tup.Item1;
            string filePath = tup.Item2;
            IFileSystem f = tup.Item1.FileSystem;
            if (f == null)
            {
                logger.Error("Unable to open filesystem for: {0}", FileName);
                return;
            }
            long filesize = 0;
            if (folder.CloudID == null) // Local Access
            {
                if (!File.Exists(FileName))
                {
                    logger.Error("File does not exist: {0}", FileName);
                    return;
                }

                int numAttempts = 0;

                // Wait 1 minute before giving up on trying to access the file
                while ((filesize = CanAccessFile(FileName)) == 0 && (numAttempts < 60))
                {
                    numAttempts++;
                    Thread.Sleep(1000);
                    logger.Error($@"Failed to access, (or filesize is 0) Attempt # {numAttempts}, {FileName}");
                }

                // if we failed to access the file, get ouuta here
                if (numAttempts >= 60)
                {
                    logger.Error("Could not access file: " + FileName);
                    return;
                }
            }


            FileSystemResult<IObject> source = f.Resolve(FileName);
            if (source == null || !source.IsOk || !(source.Result is IFile))
            {
                logger.Error("Could not access file: " + FileName);
                return;
            }
            IFile source_file = (IFile) source;
            if (folder.CloudID.HasValue)
                filesize = source_file.Size;
            nshareID = folder.ImportFolderID;

            // check if we have already processed this file

            // check if we have already processed this file
            SVR_VideoLocal_Place vlocalplace = Repo.VideoLocalPlace.GetByFilePathAndImportFolderID(filePath, nshareID);
            SVR_VideoLocal vlocal = null;
            var filename = Path.GetFileName(filePath);

            if (vlocalplace != null)
            {
                vlocal = vlocalplace.VideoLocal;
                if (vlocal != null)
                {
                    logger.Trace("VideoLocal record found in database: {0}", FileName);

                    // This will only happen with DB corruption, so just clean up the mess.
                    if (vlocalplace.FullServerPath == null)
                    {
                        if (vlocal.Places.Count == 1)
                        {
                            Repo.VideoLocal.Delete(vlocal);
                            vlocal = null;
                        }

                        Repo.VideoLocalPlace.Delete(vlocalplace);
                        vlocalplace = null;
                    }

                    if (vlocal != null && ForceHash)
                    {
                        vlocal.FileSize = filesize;
                        vlocal.DateTimeUpdated = DateTime.Now;
                    }
                }
            }

            if (vlocal == null)
            {
                logger.Trace("No existing VideoLocal, creating temporary record");
                vlocal = new SVR_VideoLocal
                {
                    DateTimeUpdated = DateTime.Now,
                    DateTimeCreated = DateTimeUpdated,
                    FileName = filename,
                    FileSize = filesize,
                    Hash = string.Empty,
                    CRC32 = string.Empty,
                    MD5 = source_file?.MD5?.ToUpperInvariant() ?? string.Empty,
                    SHA1 = source_file?.SHA1?.ToUpperInvariant() ?? string.Empty,
                    IsIgnored = 0,
                    IsVariation = 0
                };
            }

            if (vlocalplace == null)
            {
                logger.Trace("No existing VideoLocal_Place, creating a new record");
                vlocalplace = new SVR_VideoLocal_Place
                {
                    FilePath = filePath,
                    ImportFolderID = nshareID,
                    ImportFolderType = folder.ImportFolderType
                };
                // Make sure we have an ID
                Repo.VideoLocalPlace.Save(vlocalplace);
            }

            // check if we need to get a hash this file
            if (string.IsNullOrEmpty(vlocal.Hash) || ForceHash)
            {
                logger.Trace("No existing hash in VideoLocal, checking XRefs");
                if (!ForceHash)
                {
                    // try getting the hash from the CrossRef
                    List<CrossRef_File_Episode> crossRefs =
                        Repo.CrossRef_File_Episode.GetByFileNameAndSize(filename, vlocal.FileSize);
                    if (crossRefs.Any())
                    {
                        local.Hash = crossRefs[0].Hash;
                        local.HashSource = (int) HashSource.DirectHash;
                    }
                }

                // try getting the hash from the LOCAL cache
                if (!ForceHash && string.IsNullOrEmpty(vlocal.Hash))
                {
                    List<FileNameHash> fnhashes =
                        Repo.FileNameHash.GetByFileNameAndSize(filename, vlocal.FileSize);
                    if (fnhashes != null && fnhashes.Count > 1)
                    {
                        List<FileNameHash> fnhashes = Repo.FileNameHash.GetByFileNameAndSize(filename, filesize);
                        if (fnhashes != null && fnhashes.Count > 1)
                        {
                            // if we have more than one record it probably means there is some sort of corruption
                            // lets delete the local records
                            foreach (FileNameHash fnh in fnhashes)
                            {
                                Repo.FileNameHash.Delete(fnh.FileNameHashID);
                            }
                        }
                    }
                    // reinit this to check if we erased them
                    fnhashes = Repo.FileNameHash.GetByFileNameAndSize(filename, vlocal.FileSize);

                        // reinit this to check if we erased them
                        fnhashes = Repo.FileNameHash.GetByFileNameAndSize(filename, filesize);

                        if (fnhashes != null && fnhashes.Count == 1)
                        {
                            logger.Trace("Got hash from LOCAL cache: {0} ({1})", FileName, fnhashes[0].Hash);
                            local.Hash = fnhashes[0].Hash;
                            local.HashSource = (int) HashSource.WebCacheFileName;
                        }
                    }
                }

                if (string.IsNullOrEmpty(vlocal.Hash))
                    FillVideoHashes(vlocal);

                //Cloud and no hash, Nothing to do, except maybe Get the mediainfo....
                if (string.IsNullOrEmpty(local.Hash) && folder.CloudID.HasValue)
                {
                    logger.Trace("No Hash found for cloud " + filename +
                                 " putting in videolocal table with empty ED2K");
                    Repo.VideoLocal.Save(vlocal, false);
                    vlocalplace.VideoLocalID = vlocal.VideoLocalID;
                    Repo.VideoLocalPlace.Save(vlocalplace);
                    if (vlocalplace.RefreshMediaInfo())
                        Repo.VideoLocal.Save(vlocalplace.VideoLocal, true);
                    return;
                }

                // hash the file
                if (string.IsNullOrEmpty(local.Hash) || ForceHash)
                {
                    logger.Info("Hashing File: {0}", FileName);
                    ShokoService.CmdProcessorHasher.QueueState = PrettyDescriptionHashing;
                    DateTime start = DateTime.Now;
                    // update the VideoLocal record with the Hash, since cloud support we calculate everything
                    var hashes = FileHashHelper.GetHashInfo(FileName.Replace("/", $"{System.IO.Path.DirectorySeparatorChar}"), true, ShokoServer.OnHashProgress,
                        true, true, true);
                    TimeSpan ts = DateTime.Now - start;
                    logger.Trace("Hashed file in {0:#0.0} seconds --- {1} ({2})", ts.TotalSeconds, FileName, Utils.FormatByteSize(vlocal.FileSize));
                    local.Hash = hashes.ED2K?.ToUpperInvariant();
                    local.CRC32 = hashes.CRC32?.ToUpperInvariant();
                    local.MD5 = hashes.MD5?.ToUpperInvariant();
                    local.SHA1 = hashes.SHA1?.ToUpperInvariant();
                    local.HashSource = (int) HashSource.DirectHash;
                }

                FillMissingHashes(local);
                if (vlocalplace != null && ForceHash)
                {
                    using (var vupd = Repo.VideoLocal.BeginAddOrUpdate(() => Repo.VideoLocal.GetByID(vlocalplace.VideoLocalID)))
                    { 
                        vupd.Entity.ForceMergeInfoFrom_RA(local);
                        vupd.Commit();
                    }
                }
                else if (vlocalplace == null)
                {
                    using (var vupd = Repo.VideoLocal.BeginAddOrUpdate(() => Repo.VideoLocal.GetByHashAndSize(local.Hash, filesize), ()=>local))
                    {
                        if (vupd.Original != null && ForceHash)
                        {
                            vupd.Entity.ForceMergeInfoFrom_RA(local);
                        }

                        vlocal = vupd.Commit();
                        using (var vlupd = Repo.VideoLocal_Place.BeginAdd())
                        {
                            vlupd.Entity.FilePath = filePath;
                            vlupd.Entity.ImportFolderID = nshareID;
                            vlupd.Entity.ImportFolderType = folder.ImportFolderType;
                            vlupd.Entity.VideoLocalID = vupd.Entity.VideoLocalID;
                            vlocalplace = vlupd.Commit();
                        }
                    }
                }

                bool duplicated = false;
                List<SVR_VideoLocal_Place> preps = vlocal.Places.Where(a => a.ImportFolder.CloudID == folder.CloudID && !vlocalplace.FullServerPath.Equals(a.FullServerPath)).ToList();
                foreach (var prep in preps)
                {
                    logger.Trace("Found existing VideoLocal with hash, merging info from it");
                    // Aid with hashing cloud. Merge hashes and save, regardless of duplicate file
                    changed = tlocal.MergeInfoFrom(vlocal);
                    vlocal = tlocal;

                    List<SVR_VideoLocal_Place> preps = vlocal.Places.Where(
                        a => a.ImportFolder.CloudID == folder.CloudID &&
                             !vlocalplace.FullServerPath.Equals(a.FullServerPath)).ToList();
                    foreach (var prep in preps)
                    {
                        if (prep == null) continue;
                        // clean up, if there is a 'duplicate file' that is invalid, remove it.
                        if (prep.FullServerPath == null)
                        {
                            Repo.VideoLocalPlace.Delete(prep);
                        }
                        else
                        {
                            FileSystemResult dupFileSystemResult =
                                prep.ImportFolder?.FileSystem?.Resolve(prep.FullServerPath);
                            if (dupFileSystemResult == null || !dupFileSystemResult.IsOk)
                                Repo.VideoLocalPlace.Delete(prep);
                        }
                    }

                    var dupPlace = vlocal.Places.FirstOrDefault(
                        a => a.ImportFolder.CloudID == folder.CloudID &&
                             !vlocalplace.FullServerPath.Equals(a.FullServerPath));

                    if (dupPlace != null)
                    {
                        logger.Warn("Found Duplicate File");
                        logger.Warn("---------------------------------------------");
                        logger.Warn($"New File: {vlocalplace.FullServerPath}");
                        logger.Warn($"Existing File: {dupPlace.FullServerPath}");
                        logger.Warn("---------------------------------------------");

                        // check if we have a record of this in the database, if not create one
                        List<DuplicateFile> dupFiles = Repo.DuplicateFile.GetByFilePathsAndImportFolder(
                            vlocalplace.FilePath,
                            dupPlace.FilePath,
                            vlocalplace.ImportFolderID, dupPlace.ImportFolderID);
                        if (dupFiles.Count == 0)
                            dupFiles = Repo.DuplicateFile.GetByFilePathsAndImportFolder(dupPlace.FilePath,
                                vlocalplace.FilePath, dupPlace.ImportFolderID, vlocalplace.ImportFolderID);

                        if (dupFiles.Count == 0)
                        {
                            DuplicateFile dup = new DuplicateFile
                            {
                                DateTimeUpdated = DateTime.Now,
                                FilePathFile1 = vlocalplace.FilePath,
                                FilePathFile2 = dupPlace.FilePath,
                                ImportFolderIDFile1 = vlocalplace.ImportFolderID,
                                ImportFolderIDFile2 = dupPlace.ImportFolderID,
                                Hash = vlocal.Hash
                            };
                            Repo.DuplicateFile.Save(dup);
                        }
                    }
                }

                foreach (var prep in preps)
                {
                    // delete the VideoLocal record
                    logger.Warn("Found Duplicate File");
                    logger.Warn("---------------------------------------------");
                    logger.Warn($"New File: {vlocalplace.FullServerPath}");
                    logger.Warn($"Existing File: {prep.FullServerPath}");
                    logger.Warn("---------------------------------------------");

                    List<DuplicateFile> dupFiles = Repo.DuplicateFile.GetByFilePathsAndImportFolderCheckBoth(vlocalplace.FilePath, prep.FilePath, vlocalplace.ImportFolderID, prep.ImportFolderID);
                    if (dupFiles.Count == 0)
                    {
                        using (var upd = Repo.DuplicateFile.BeginAdd())
                        {
                            upd.Entity.DateTimeUpdated = DateTime.Now;
                            upd.Entity.FilePathFile1 = vlocalplace.FilePath;
                            upd.Entity.FilePathFile2 = prep.FilePath;
                            upd.Entity.ImportFolderIDFile1 = vlocalplace.ImportFolderID;
                            upd.Entity.ImportFolderIDFile2 = prep.ImportFolderID;
                            upd.Entity.Hash = vlocal.Hash;
                            upd.Commit();
                        }
                    }

                    duplicated = true;
                }

                if (duplicated)
                {
                    CommandRequest_ProcessFile cr_procfile3 = new CommandRequest_ProcessFile(vlocal.VideoLocalID, false);
                    cr_procfile3.Save();
                    return;
                }

                // also save the filename to hash record
                // replace the existing records just in case it was corrupt
                FileNameHash fnhash;
                List<FileNameHash> fnhashes2 =
                    Repo.FileNameHash.GetByFileNameAndSize(filename, vlocal.FileSize);
                if (fnhashes2 != null && fnhashes2.Count > 1)
                {
                    FileNameHash fnhash = null;
                    List<FileNameHash> fnhashes2 = Repo.FileNameHash.GetByFileNameAndSize(vlocal.FileName, vlocal.FileSize);
                    if (fnhashes2.Count > 0)
                    {
                        // if we have more than one record it probably means there is some sort of corruption
                        // lets delete the local records
                        if (fnhashes2.Count > 1)
                        {
                            for (int x = 1; x < fnhashes2.Count; x++)
                                Repo.FileNameHash.Delete(fnhashes2[x]);
                        }

                        fnhash = fnhashes2[2];
                    }

                fnhash.FileName = filename;
                fnhash.FileSize = vlocal.FileSize;
                fnhash.Hash = vlocal.Hash;
                fnhash.DateTimeUpdated = DateTime.Now;
                Repo.FileNameHash.Save(fnhash);
            }
            else
            {
                FillMissingHashes(vlocal);
            }


            if ((vlocal.Media == null) || vlocal.MediaVersion < SVR_VideoLocal.MEDIA_VERSION || vlocal.Duration == 0)
            {
                using (var upd = Repo.VideoLocal.BeginAddOrUpdate(()=>Repo.VideoLocal.GetByID(vlocal.VideoLocalID)))
                {
                    if (vlocalplace.RefreshMediaInfo(upd.Entity))
                        vlocal=upd.Commit(true);

                }
            }

            // now add a command to process the file
            CommandRequest_ProcessFile cr_procfile = new CommandRequest_ProcessFile(vlocal.VideoLocalID, false);
            cr_procfile.Save();
        }

        private void FillMissingHashes(SVR_VideoLocal vlocal)
        {
            bool needcrc32 = string.IsNullOrEmpty(vlocal.CRC32);
            bool needmd5 = string.IsNullOrEmpty(vlocal.MD5);
            bool needsha1 = string.IsNullOrEmpty(vlocal.SHA1);
            if (needcrc32 || needmd5 || needsha1)
                FillVideoHashes_RA(vlocal);
            needcrc32 = string.IsNullOrEmpty(vlocal.CRC32);
            needmd5 = string.IsNullOrEmpty(vlocal.MD5);
            needsha1 = string.IsNullOrEmpty(vlocal.SHA1);
            if (needcrc32 || needmd5 || needsha1)
            {
                ShokoService.CmdProcessorHasher.QueueState = PrettyDescriptionHashing;
                DateTime start = DateTime.Now;
                List<string> tp = new List<string>();
                if (needsha1)
                    tp.Add("SHA1");
                if (needmd5)
                    tp.Add("MD5");
                if (needcrc32)
                    tp.Add("CRC32");
                logger.Trace("Calculating missing {1} hashes for: {0}", FileName, string.Join(",", tp));
                // update the VideoLocal record with the Hash, since cloud support we calculate everything
                Hashes hashes = FileHashHelper.GetHashInfo(FileName.Replace("/", $"{System.IO.Path.DirectorySeparatorChar}"), true, ShokoServer.OnHashProgress,
                    needcrc32, needmd5, needsha1);
                TimeSpan ts = DateTime.Now - start;
                logger.Trace("Hashed file in {0:#0.0} seconds --- {1} ({2})", ts.TotalSeconds,
                    FileName, Utils.FormatByteSize(vlocal.FileSize));
                if (String.IsNullOrEmpty(vlocal.Hash))
                    vlocal.Hash = hashes.ED2K?.ToUpperInvariant();
                if (needsha1)
                    vlocal.SHA1 = hashes.SHA1?.ToUpperInvariant();
                if (needmd5)
                    vlocal.MD5 = hashes.MD5?.ToUpperInvariant();
                if (needcrc32)
                    vlocal.CRC32 = hashes.CRC32?.ToUpperInvariant();
                AzureWebAPI.Send_FileHash(new List<SVR_VideoLocal> {vlocal});
            }
        }

        private void FillHashesAgainstVideoLocalRepo(SVR_VideoLocal v)
        {
            if (!string.IsNullOrEmpty(v.ED2KHash))
            {
                SVR_VideoLocal n = Repo.VideoLocal.GetByHash(v.ED2KHash);
                if (n != null)
                {
                    if (!string.IsNullOrEmpty(n.CRC32))
                        v.CRC32 = n.CRC32.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(n.MD5))
                        v.MD5 = n.MD5.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(n.SHA1))
                        v.SHA1 = n.SHA1.ToUpperInvariant();
                    return;
                }
            }
            if (!string.IsNullOrEmpty(v.SHA1))
            {
                SVR_VideoLocal n = Repo.VideoLocal.GetBySHA1(v.SHA1);
                if (n != null)
                {
                    if (!string.IsNullOrEmpty(n.CRC32))
                        v.CRC32 = n.CRC32.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(n.MD5))
                        v.MD5 = n.MD5.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(v.ED2KHash))
                        v.ED2KHash = n.ED2KHash.ToUpperInvariant();
                    return;
                }
            }
            if (!string.IsNullOrEmpty(v.MD5))
            {
                SVR_VideoLocal n = Repo.VideoLocal.GetByMD5(v.MD5);
                if (n != null)
                {
                    if (!string.IsNullOrEmpty(n.CRC32))
                        v.CRC32 = n.CRC32.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(n.SHA1))
                        v.SHA1 = n.SHA1.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(v.ED2KHash))
                        v.ED2KHash = n.ED2KHash.ToUpperInvariant();
                }
            }
        }

        private void FillHashesAgainstAniDBRepo(SVR_VideoLocal v)
        {
            if (!string.IsNullOrEmpty(v.ED2KHash))
            {
                SVR_AniDB_File f = Repo.AniDB_File.GetByHash(v.ED2KHash);
                if (f != null)
                {
                    if (!string.IsNullOrEmpty(f.CRC))
                        v.CRC32 = f.CRC.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(f.SHA1))
                        v.SHA1 = f.SHA1.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(f.MD5))
                        v.MD5 = f.MD5.ToUpperInvariant();
                    return;
                }
            }
            if (!string.IsNullOrEmpty(v.SHA1))
            {
                SVR_AniDB_File f = Repo.AniDB_File.GetBySHA1(v.SHA1);
                if (f != null)
                {
                    if (!string.IsNullOrEmpty(f.CRC))
                        v.CRC32 = f.CRC.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(f.Hash))
                        v.ED2KHash = f.Hash.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(f.MD5))
                        v.MD5 = f.MD5.ToUpperInvariant();
                    return;
                }
            }
            if (!string.IsNullOrEmpty(v.MD5))
            {
                SVR_AniDB_File f = Repo.AniDB_File.GetByMD5(v.MD5);
                if (f != null)
                {
                    if (!string.IsNullOrEmpty(f.CRC))
                        v.CRC32 = f.CRC.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(f.Hash))
                        v.ED2KHash = f.Hash.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(f.SHA1))
                        v.SHA1 = f.SHA1.ToUpperInvariant();
                }
            }
        }

        private void FillHashesAgainstWebCache(SVR_VideoLocal v)
        {
            if (!string.IsNullOrEmpty(v.ED2KHash))
            {
                List<Azure_FileHash> ls = AzureWebAPI.Get_FileHash(FileHashType.ED2K, v.ED2KHash) ??
                                          new List<Azure_FileHash>();
                ls = ls.Where(a => !string.IsNullOrEmpty(a.CRC32) && !string.IsNullOrEmpty(a.MD5) &&
                                   !string.IsNullOrEmpty(a.SHA1))
                    .ToList();
                if (ls.Count > 0)
                {
                    if (!string.IsNullOrEmpty(ls[0].SHA1))
                        v.SHA1 = ls[0].SHA1.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(ls[0].CRC32))
                        v.CRC32 = ls[0].CRC32.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(ls[0].MD5))
                        v.MD5 = ls[0].MD5.ToUpperInvariant();
                    return;
                }
            }
            if (!string.IsNullOrEmpty(v.SHA1))
            {
                List<Azure_FileHash> ls = AzureWebAPI.Get_FileHash(FileHashType.SHA1, v.SHA1) ??
                                          new List<Azure_FileHash>();
                ls = ls.Where(a => !string.IsNullOrEmpty(a.CRC32) && !string.IsNullOrEmpty(a.MD5) &&
                                   !string.IsNullOrEmpty(a.ED2K))
                    .ToList();
                if (ls.Count > 0)
                {
                    if (!string.IsNullOrEmpty(ls[0].ED2K))
                        v.ED2KHash = ls[0].ED2K.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(ls[0].CRC32))
                        v.CRC32 = ls[0].CRC32.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(ls[0].MD5))
                        v.MD5 = ls[0].MD5.ToUpperInvariant();
                    return;
                }
            }
            if (!string.IsNullOrEmpty(v.MD5))
            {
                List<Azure_FileHash> ls = AzureWebAPI.Get_FileHash(FileHashType.MD5, v.MD5) ??
                                          new List<Azure_FileHash>();
                ls = ls.Where(a => !string.IsNullOrEmpty(a.CRC32) && !string.IsNullOrEmpty(a.SHA1) &&
                                   !string.IsNullOrEmpty(a.ED2K))
                    .ToList();
                if (ls.Count > 0)
                {
                    if (!string.IsNullOrEmpty(ls[0].ED2K))
                        v.ED2KHash = ls[0].ED2K.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(ls[0].CRC32))
                        v.CRC32 = ls[0].CRC32.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(ls[0].SHA1))
                        v.SHA1 = ls[0].SHA1.ToUpperInvariant();
                }
            }
        }

        private void FillVideoHashes_RA(SVR_VideoLocal v)
        {
            if (string.IsNullOrEmpty(v.CRC32) || string.IsNullOrEmpty(v.MD5) || string.IsNullOrEmpty(v.SHA1))
                FillHashesAgainstVideoLocalRepo(v);
            if (string.IsNullOrEmpty(v.CRC32) || string.IsNullOrEmpty(v.MD5) || string.IsNullOrEmpty(v.SHA1))
                FillHashesAgainstAniDBRepo(v);
            if (string.IsNullOrEmpty(v.CRC32) || string.IsNullOrEmpty(v.MD5) || string.IsNullOrEmpty(v.SHA1))
                FillHashesAgainstWebCache(v);
        }


        /// <summary>
        /// This should generate a unique key for a command
        /// It will be used to check whether the command has already been queued before adding it
        /// </summary>
        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_HashFile_{FileName}";
        }

        public override bool InitFromDB(Shoko.Models.Server.CommandRequest cq)
        {
            CommandID = cq.CommandID;
            CommandRequestID = cq.CommandRequestID;
            Priority = cq.Priority;
            CommandDetails = cq.CommandDetails;
            DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(CommandDetails);

                // populate the fields
                FileName = TryGetProperty(docCreator, "CommandRequest_HashFile", "FileName");
                ForceHash = bool.Parse(TryGetProperty(docCreator, "CommandRequest_HashFile", "ForceHash"));
            }

            if (FileName.Trim().Length > 0)
                return true;
            return false;
        }

        public override CommandRequest ToDatabaseObject()
        {
            GenerateCommandID();

            CommandRequest cq = new CommandRequest
            {
                CommandID = CommandID,
                CommandType = CommandType,
                Priority = Priority,
                CommandDetails = ToXML(),
                DateTimeUpdated = DateTime.Now
            };
            return cq;
        }
    }
}
