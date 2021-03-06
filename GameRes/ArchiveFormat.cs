//! \file       ArchiveFormat.cs
//! \date       Sun Dec 04 15:19:21 2016
//! \brief      Abstract base class representing archive resource.
//
// Copyright (C) 2014-2016 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.IO;

namespace GameRes
{
    /// <summary>
    /// Abstract base class for archive resource implementations.
    /// </summary>
    public abstract class ArchiveFormat : IResource
    {
        public override string Type { get { return "archive"; } }

        /// <summary>
        /// Whether archive file system could contain subdirectories.
        /// </summary>
        public abstract bool IsHierarchic { get; }

        public abstract ArcFile TryOpen (ArcView view);

        /// <summary>
        /// Extract file referenced by <paramref name="entry"/> into current directory.
        /// </summary>
        public void Extract (ArcFile file, Entry entry)
        {
            using (var input = OpenEntry (file, entry))
            using (var output = CreateFile (entry.Name))
                input.CopyTo (output);
        }

        /// <summary>
        /// Open file referenced by <paramref name="entry"/> as Stream.
        /// </summary>
        public virtual Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
        }

        /// <summary>
        /// Open <paramref name="entry"> as image. Throws InvalidFormatException if entry is not an image.
        /// </summary>
        public virtual IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            try
            {
                return new ImageFormatDecoder (input);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Create file corresponding to <paramref name="entry"/> in current directory and open it
        /// for writing. Overwrites existing file, if any.
        /// </summary>
        static public Stream CreateFile (string filename)
        {
            filename = CreatePath (filename);
            if (File.Exists (filename))
            {
                // query somehow whether to overwrite existing file or not.
            }
            return File.Create (filename);
        }

        static public string CreatePath (string filename)
        {
            string dir = Path.GetDirectoryName (filename);
            if (!string.IsNullOrEmpty (dir)) // check for malformed filenames
            {
                string root = Path.GetPathRoot (dir);
                if (!string.IsNullOrEmpty (root))
                {
                    dir = dir.Substring (root.Length); // strip root
                }
                string cwd = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar;
                dir = Path.GetFullPath (dir);
                filename = Path.GetFileName (filename);
                // check whether filename would reside within current directory
                if (dir.StartsWith (cwd, StringComparison.OrdinalIgnoreCase))
                {
                    // path looks legit, create it
                    Directory.CreateDirectory (dir);
                    filename = Path.Combine (dir, filename);
                }
            }
            return filename;
        }

        /// <summary>
        /// Create resource within stream <paramref name="file"/> containing entries from the
        /// supplied <paramref name="list"/> and applying necessary <paramref name="options"/>.
        /// </summary>
        public virtual void Create (Stream file, IEnumerable<Entry> list, ResourceOptions options = null,
                                    EntryCallback callback = null)
        {
            throw new NotImplementedException ("ArchiveFormat.Create is not implemented");
        }

        /// <summary>
        /// Whether <paramref name="count"/> represents legit number of files in archive.
        /// </summary>
        public static bool IsSaneCount (int count)
        {
            return count > 0 && count < 0x20000;
        }
    }

    public enum ArchiveOperation
    {
        Abort,
        Skip,
        Continue,
    }

    public delegate ArchiveOperation EntryCallback (int num, Entry entry, string description);

    public class OverwriteEventArgs : EventArgs
    {
        public string Filename { get; set; }
        public bool  Overwrite { get; set; }
    }
}
