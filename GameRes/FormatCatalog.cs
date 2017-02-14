//! \file       FormatCatalog.cs
//! \date       Wed Sep 16 22:51:11 2015
//! \brief      game resources formats catalog class.
//
// Copyright (C) 2014-2015 by morkt
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
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using GameRes.Collections;
using System.Runtime.Serialization.Formatters.Binary;
using GameRes.Compression;

namespace GameRes
{
    public sealed class FormatCatalog
    {
        private static readonly FormatCatalog m_instance = new FormatCatalog();

        #pragma warning disable 649
        [ImportMany(typeof(ArchiveFormat))]
        private IEnumerable<ArchiveFormat>  m_arc_formats;
        [ImportMany(typeof(ImageFormat))]
        private IEnumerable<ImageFormat>    m_image_formats;
        [ImportMany(typeof(AudioFormat))]
        private IEnumerable<AudioFormat>    m_audio_formats;
        [ImportMany(typeof(ScriptFormat))]
        private IEnumerable<ScriptFormat>   m_script_formats;
        #pragma warning restore 649

        private MultiValueDictionary<string, IResource> m_extension_map = new MultiValueDictionary<string, IResource>();
        private MultiValueDictionary<uint, IResource> m_signature_map = new MultiValueDictionary<uint, IResource>();

        private Dictionary<string, string> m_game_map = new Dictionary<string, string>();

        /// <summary> The only instance of this class.</summary>
        public static FormatCatalog       Instance      { get { return m_instance; } }

        public IEnumerable<ArchiveFormat> ArcFormats    { get { return m_arc_formats; } }
        public IEnumerable<ImageFormat>   ImageFormats  { get { return m_image_formats; } }
        public IEnumerable<AudioFormat>   AudioFormats  { get { return m_audio_formats; } }
        public IEnumerable<ScriptFormat>  ScriptFormats { get { return m_script_formats; } }

        public IEnumerable<IResource> Formats
        {
            get
            {
                return ((IEnumerable<IResource>)ArcFormats).Concat (ImageFormats).Concat (AudioFormats).Concat (ScriptFormats);
            }
        }

        public int CurrentSchemeVersion { get; private set; }
        public string          SchemeID { get { return "GARbroDB"; } }
        public string  AssemblyLocation { get; private set; }
        public string     DataDirectory { get { return m_gamedata_dir.Value; } }

        public Exception LastError { get; set; }

        public event ParametersRequestEventHandler  ParametersRequest;

        private Lazy<string> m_gamedata_dir;

        private FormatCatalog ()
        {
            AssemblyLocation = Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly().Location);
            m_gamedata_dir = new Lazy<string> (() => Path.Combine (AssemblyLocation, "GameData"));

            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();
            //Adds all the parts found in the same assembly as the Program class
            catalog.Catalogs.Add (new AssemblyCatalog (typeof(FormatCatalog).Assembly));
            //Adds parts matching pattern found in the directory of the assembly
            catalog.Catalogs.Add (new DirectoryCatalog (AssemblyLocation, "Arc*.dll"));

            //Create the CompositionContainer with the parts in the catalog
            using (var container = new CompositionContainer (catalog))
            {
                //Fill the imports of this object
                container.ComposeParts (this);
                AddResourceImpl (m_image_formats, container);
                AddResourceImpl (m_arc_formats, container);
                AddResourceImpl (m_audio_formats, container);
                AddResourceImpl (m_script_formats, container);
            }
        }

        private void AddResourceImpl (IEnumerable<IResource> formats, CompositionContainer container)
        {
            foreach (var impl in formats)
            {
                try
                {
                    var part = AttributedModelServices.CreatePart (impl);
                    if (part.ImportDefinitions.Any())
                        container.SatisfyImportsOnce (part);
                }
                catch (Exception X)
                {
                    System.Diagnostics.Trace.WriteLine (X.Message, impl.Tag);
                }
                foreach (var ext in impl.Extensions)
                {
                    m_extension_map.Add (ext.ToUpperInvariant(), impl);
                }
                foreach (var signature in impl.Signatures)
                {
                    m_signature_map.Add (signature, impl);
                }
            }
        }

        /// <summary>
        /// Look up filename in format registry by filename extension and return corresponding interfaces.
        /// if no formats available, return empty range.
        /// </summary>
        public IEnumerable<IResource> LookupFileName (string filename)
        {
            string ext = Path.GetExtension (filename);
            if (string.IsNullOrEmpty (ext))
                return Enumerable.Empty<IResource>();
            return LookupExtension (ext.TrimStart ('.'));
        }

        public IEnumerable<IResource> LookupExtension (string ext)
        {
            return m_extension_map.GetValues (ext.ToUpperInvariant(), true);
        }

        public IEnumerable<Type> LookupExtension<Type> (string ext) where Type : IResource
        {
            return LookupExtension (ext).OfType<Type>();
        }

        public IEnumerable<IResource> LookupSignature (uint signature)
        {
            return m_signature_map.GetValues (signature, true);
        }

        public IEnumerable<Type> LookupSignature<Type> (uint signature) where Type : IResource
        {
            return LookupSignature (signature).OfType<Type>();
        }

        /// <summary>
        /// Enumerate resources matching specified <paramref name="signature"/> and filename extension.
        /// </summary>
        internal IEnumerable<ResourceType> FindFormats<ResourceType> (string filename, uint signature) where ResourceType : IResource
        {
            var ext = new Lazy<string> (() => Path.GetExtension (filename).TrimStart ('.').ToLowerInvariant(), false);
            var tried = Enumerable.Empty<ResourceType>();
            for (;;)
            {
                var range = LookupSignature<ResourceType> (signature);
                if (tried.Any())
                    range = range.Except (tried);
                // check formats that match filename extension first
                if (range.Skip (1).Any()) // if range.Count() > 1
                    range = range.OrderByDescending (f => f.Extensions.Any (e => e == ext.Value));
                foreach (var impl in range)
                {
                    yield return impl;
                }
                if (0 == signature)
                    break;
                signature = 0;
                tried = range;
            }
        }

        /// <summary>
        /// Create GameRes.Entry corresponding to <paramref name="filename"/> extension.
        /// </summary>
        /// <exception cref="System.ArgumentException">May be thrown if filename contains invalid
        /// characters.</exception>
        public EntryType Create<EntryType> (string filename) where EntryType : Entry, new()
        {
            EntryType entry = null;
            var formats = LookupFileName (filename);
            if (formats.Any())
                entry = formats.First().Create<EntryType>();
            if (null == entry)
                entry = new EntryType();
            entry.Name = filename;
            return entry;
        }

        public string GetTypeFromName (string filename)
        {
            var formats = LookupFileName (filename);
            if (formats.Any())
                return formats.First().Type;
            return "";
        }

        public void InvokeParametersRequest (object source, ParametersRequestEventArgs args)
        {
            if (null != ParametersRequest)
                ParametersRequest (source, args);
        }

        /// <summary>
        /// Read first 4 bytes from stream and return them as 32-bit signature.
        /// </summary>
        public static uint ReadSignature (Stream file)
        {
            file.Position = 0;
            uint signature = (byte)file.ReadByte();
            signature |= (uint)file.ReadByte() << 8;
            signature |= (uint)file.ReadByte() << 16;
            signature |= (uint)file.ReadByte() << 24;
            return signature;
        }

        /// <summary>
        /// Look up game title based on archive <paramref name="arc_name"/> and files matching
        /// <paramref name="pattern"/> in the same directory as archive.
        /// </summary>
        /// <returns>Game title, or null if no match was found.</returns>
        public string LookupGame (string arc_name, string pattern = "*.exe")
        {
            string title;
            if (m_game_map.TryGetValue (Path.GetFileName (arc_name), out title))
                return title;
            pattern = VFS.CombinePath (VFS.GetDirectoryName (arc_name), pattern);
            foreach (var file in VFS.GetFiles (pattern).Select (e => Path.GetFileName (e.Name)))
            {
                if (m_game_map.TryGetValue (file, out title))
                    return title;
            }
            return null;
        }

        public void DeserializeScheme (Stream input)
        {
            int version = GetSerializedSchemeVersion (input);
            if (version <= CurrentSchemeVersion)
                return;
            using (var zs = new ZLibStream (input, CompressionMode.Decompress, true))
            {
                var bin = new BinaryFormatter();
                var db = (SchemeDataBase)bin.Deserialize (zs);

                foreach (var format in Formats)
                {
                    ResourceScheme scheme;
                    if (db.SchemeMap.TryGetValue (format.Tag, out scheme))
                        format.Scheme = scheme;
                }
                CurrentSchemeVersion = db.Version;
                if (db.GameMap != null)
                    m_game_map = db.GameMap;
            }
        }

        public void SerializeScheme (Stream output)
        {
            var db = new SchemeDataBase {
                Version = CurrentSchemeVersion,
                SchemeMap = new Dictionary<string, ResourceScheme>(),
                GameMap = m_game_map,
            };
            foreach (var format in Formats)
            {
                var scheme = format.Scheme;
                if (null != scheme)
                    db.SchemeMap[format.Tag] = scheme;
            }
            SerializeScheme (output, db);
        }

        public void SerializeScheme (Stream output, SchemeDataBase db)
        {
            using (var writer = new BinaryWriter (output, System.Text.Encoding.UTF8, true))
            {
                writer.Write (SchemeID.ToCharArray());
                writer.Write (db.Version);
            }
            var bin = new BinaryFormatter();
            using (var zs = new ZLibStream (output, CompressionMode.Compress, true))
                bin.Serialize (zs, db);
        }

        public int GetSerializedSchemeVersion (Stream input)
        {
            using (var reader = new BinaryReader (input, System.Text.Encoding.UTF8, true))
            {
                var header = reader.ReadChars (SchemeID.Length);
                if (!header.SequenceEqual (SchemeID))
                    throw new FormatException ("Invalid serialization file");
                return reader.ReadInt32();
            }
        }
    }

    [Serializable]
    public class SchemeDataBase
    {
        public int Version;

        public Dictionary<string, ResourceScheme>   SchemeMap;
        public Dictionary<string, string>           GameMap;
    }
}
