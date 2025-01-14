using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SharpCompress.Archives.Zip;

namespace Lumper.Lib.BSP.Struct
{
    public class PakFileEntry
    {
        public PakFileEntry(ZipArchiveEntry entry)
        {
            _entry = entry;
            Key = entry.Key;
        }

        public PakFileEntry(string key, Stream stream)
        {
            Key = key;
            _dataStream = stream;
        }

        [JsonIgnore]
        private readonly ZipArchiveEntry _entry;
        public string Key { get; set; }
        [JsonIgnore]
        public Stream? _dataStream = null;
        [JsonIgnore]
        public Stream DataStream
        {
            get
            {
                if (_dataStream is null)
                    return _entry.OpenEntryStream();
                else
                    return _dataStream;
            }
            set
            {
                if (_dataStream is not null)
                {
                    _dataStream.Close();
                    _dataStream.Dispose();
                }
                _dataStream = value;
            }
        }

        public bool IsModified { get => _dataStream != null; }

        public byte[] HashMD5
        {
            get
            {
                var stream = DataStream;
                if (stream.CanSeek)
                    stream.Seek(0, SeekOrigin.Begin);
                return MD5.Create().ComputeHash(stream);
            }
        }
    }
}