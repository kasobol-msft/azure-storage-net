using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Test.Perf;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.Queue;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestApp1;

namespace ConsoleApp1
{
    class Program
    {
        private const string StorageConnectionString = "DefaultEndpointsProtocol=https;AccountName=kasoboltest;AccountKey=gYkU83eFsuvDgFHOB/aZBfrdAdRO2UqdaAPdSWnvpHqRLKatNPdXS8SZ8uF8QeoEICAfZQOAscT2u+4t8LiMxw==;EndpointSuffix=core.windows.net";


        static void Main(string[] args)
        {
            OperationContext.GlobalSendingRequest += OperationContext_GlobalSendingRequest;
            OperationContext.GlobalResponseReceived += OperationContext_GlobalResponseReceived;
            ThreadPool.SetMinThreads(100, 100);
            Console.WriteLine("Hello World!");
            CreateFile().GetAwaiter().GetResult();
            Old();
            //await OldAsync();
            //await New();
            Console.WriteLine("Done!");
        }

        private static void OperationContext_GlobalResponseReceived(object sender, RequestEventArgs e)
        {
            Console.WriteLine($"{DateTime.UtcNow.ToString("O")} {e.Request.Headers.GetValues("x-ms-client-request-id").FirstOrDefault()} Received_{e.Response.StatusCode}");
        }

        private static void OperationContext_GlobalSendingRequest(object sender, RequestEventArgs e)
        {
            Console.WriteLine($"{DateTime.UtcNow.ToString("O")} {e.Request.Headers.GetValues("x-ms-client-request-id").FirstOrDefault()} Sending_{e.Request.RequestUri}");
        }

        private static async Task CreateFile()
        {
            var path = "C:\\tmp\\largefile.txt";
            if(File.Exists(path))
            {
                return;
            }
            using (var stream = File.OpenWrite(path)) {
                using (var writer = new StreamWriter(stream))
                {
                    while (writer.BaseStream.Length <= 1024L*1024L*10L)
                    {
                        await writer.WriteLineAsync(Guid.NewGuid().ToString());
                    }
                }
            }
        }

        private static void Old()
        {
            var storageAccount = CloudStorageAccount.Parse(StorageConnectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("test10gb");
            // Create the Key to be used for wrapping.
            // This code creates a random encryption key.
            SymmetricKey aesKey = new SymmetricKey(kid: "symencryptionkey");

            // Create the encryption policy to be used for upload.
            BlobEncryptionPolicy uploadPolicy = new BlobEncryptionPolicy(key: aesKey, keyResolver: null);
            BlobRequestOptions blobRequestOptions = new BlobRequestOptions()
            {
                EncryptionPolicy = uploadPolicy
            };
            container.CreateIfNotExists();

            List<Thread> threads = new List<Thread>();
            for (int i = 0; i < 10; i++)
            {
                int threadNo = i;
                Thread thread = new Thread(() => DoWorkSync(threadNo, blobRequestOptions, container));
                threads.Add(thread);
                thread.Start();
            }
            for (int i = 10; i < 20; i++)
            {
                int threadNo = i;
                Thread thread = new Thread(() => DoWorkSync(threadNo, null, container));
                threads.Add(thread);
                thread.Start();
            }
            threads.ForEach(t => t.Join());
        }

        private static void DoWorkSync(int threadNo, BlobRequestOptions blobRequestOptions, CloudBlobContainer container)
        {
            Random random = new Random(Environment.TickCount + threadNo);
            while (true)
            {
                long size = random.Next(16 * 1024, 4 * 16 * 1024);
                Guid name = Guid.NewGuid();
                OperationContext operationContext = new OperationContext();
                Console.WriteLine($"thread {threadNo} size {size} blobname {name} reqid {operationContext.ClientRequestID}");
                var blob = container.GetBlockBlobReference(name.ToString());
                blob.StreamWriteSizeInBytes = 16 * 1024;

                try
                {
                    using (var fileStream = RandomStream.Create(size))
                    {
                        using (CloudBlobStream targetStream = blob.OpenWrite(null, blobRequestOptions, operationContext))
                        {
                            fileStream.CopyTo(targetStream);
                            Console.WriteLine($"{DateTime.UtcNow.ToString("O")} {operationContext.ClientRequestID} I'm_after_CopyTo_but_inside_Using");
                        }
                        var _ = fileStream.Length;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"error: thread {threadNo} size {size} blobname {name} reqid {operationContext.ClientRequestID}");
                    throw e;
                }
            }
        }

        private static async Task OldAsync()
        {
            var storageAccount = CloudStorageAccount.Parse(StorageConnectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("test10gb");
            // Create the Key to be used for wrapping.
            // This code creates a random encryption key.
            SymmetricKey aesKey = new SymmetricKey(kid: "symencryptionkey");

            // Create the encryption policy to be used for upload.
            BlobEncryptionPolicy uploadPolicy = new BlobEncryptionPolicy(key: aesKey, keyResolver: null);
            BlobRequestOptions blobRequestOptions = new BlobRequestOptions()
            {
                EncryptionPolicy = uploadPolicy
            };
            container.CreateIfNotExists();

            List<Task> threads = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                int threadNo = i;
                Task thread = DoWorkAsync(threadNo, blobRequestOptions, container);
                threads.Add(thread);
            }
            for (int i = 10; i < 20; i++)
            {
                int threadNo = i;
                Task thread = DoWorkAsync(threadNo, null, container);
                threads.Add(thread);
            }
            await Task.WhenAny(threads);
        }

        private static async Task DoWorkAsync(int threadNo, BlobRequestOptions blobRequestOptions, CloudBlobContainer container)
        {
            Random random = new Random(Environment.TickCount + threadNo);
            while (true)
            {
                long size = random.Next(16 * 1024, 4 * 16 * 1024);
                Guid name = Guid.NewGuid();
                Console.WriteLine($"task {threadNo} size {size} blobname {name}");
                var blob = container.GetBlockBlobReference(name.ToString());
                blob.StreamWriteSizeInBytes = 16 * 1024;

                try
                {
                    using (var fileStream = RandomStream.Create(size))
                    {
                        await blob.UploadFromStreamAsync(fileStream, null, options: blobRequestOptions, null);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"error: task {threadNo} size {size} blobname {name}");
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                    throw e;
                }
            }
        }

        private static async Task New()
        {
            var blobServiceClient = new BlobServiceClient(StorageConnectionString);
            var container = blobServiceClient.GetBlobContainerClient("test10gb2");
            await container.CreateIfNotExistsAsync();
            var blobClient = container.GetBlockBlobClient("largeblob.txt");
            var path = "C:\\tmp\\largefile.txt";
            using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                await blobClient.UploadAsync(stream);
            }
        }
    }
}
