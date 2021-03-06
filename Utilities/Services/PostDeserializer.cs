using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;
using Newtonsoft.Json;
using QCUtilities.Entities;
using QCUtilities.Interfaces;

namespace QCUtilities
{
    public class PostDeserializer : IPostLoader
    {
        public List<Post> Posts { get; }

        public PostDeserializer(IDiskArchiveValidator validator, IPostCollectionValidator collectionValidator, string path, string xsd)
        {
            ValidateDirectory(validator, path, xsd);

            Posts = new List<Post>();
            foreach (var fileName in Directory.GetFiles(path))
            {
                var ser = new XmlSerializer(typeof(Post));
                Post post = null;
                using (Stream reader = new FileStream(fileName, FileMode.Open))
                {
                    post = (Post)ser.Deserialize(reader);
                }
                Posts.Add(post);
            }

            ValidatePostCollection(collectionValidator, Posts);

            // We want posts to be shown in chronological order, newest-to-oldest, so we just do this here because it's silly to redo it every time we reload.
            Posts = Posts.OrderByDescending(post => post.Date).ToList();
        }

        private void ValidateDirectory(IDiskArchiveValidator archiveValidator, string path, string xsd)
        {
            if (!archiveValidator.DirectoryExists(path))
            {
                throw new DirectoryNotFoundException($"Directory not found. Make sure the path is correct.");
            }

            if (!archiveValidator.AFileExists(path))
            {
                throw new FileNotFoundException($"No files found in directory. Make sure the path is correct.");
            }

            if (!archiveValidator.FilesValid(path, xsd))
            {
                throw new XmlException($"Parse or schema errors found in files!");
            }
        }

        private void ValidatePostCollection(IPostCollectionValidator collectionValidator, List<Post> postcollection)
        {
            if (!collectionValidator.CollectionContainsUniqueURLS(postcollection, out string errorMsg))
            {
                throw new FileLoadException(errorMsg);
            }
        }
    }

    // Note: This is currently very inefficient because it ends up reading the same files multiple times.
    // This should technically be fixed, but it's also irrelevant in terms of performance.
    public class DiskArchiveValidator : IDiskArchiveValidator
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public bool AFileExists(string path)
        {
            return Directory.GetFiles(path).Any();
        }

        public bool FilesValid(string path, string xsd)
        {
            var schema = new XmlSchemaSet();
            schema.Add("", xsd);

            bool valid = true;

            foreach (var file in Directory.GetFiles(path))
            {
                using (var rd = XmlReader.Create(file))
                {
                    var doc = XDocument.Load(rd);

                    doc.Validate(
                        schema, (o, e) =>
                        {
                            valid = false;

                            Console.WriteLine($"{e.Severity} in file {file}: {e.Message}");
                        }
                    );
                }
            }

            return valid;
        }
    }

    public class CollectionValidator : IPostCollectionValidator
    {
        public bool CollectionContainsUniqueURLS(List<Post> postCollection, out string errorMSG)
        {
            errorMSG = "";
            if (postCollection.Count > 0)
            {
                var guiltyTitles = (
                                            from post in postCollection
                                            group post.Title by post.Title into g
                                            where g.Count() > 1
                                            let occurences= g.Count()
                                            let title=g.Key
                                            select new { title, occurences}
                                        )?.ToList();

                bool duplicateURLSFound = guiltyTitles?.Count() > 0;
                if (duplicateURLSFound)
                {
                    var postResults = JsonConvert.SerializeObject(guiltyTitles).ToString();
                    errorMSG = $"The xml contains the following duplicate URLS:\n{postResults}";
                }
            }
            return string.IsNullOrEmpty(errorMSG);
        }
    }
}
