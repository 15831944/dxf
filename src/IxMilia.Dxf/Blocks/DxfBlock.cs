﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using IxMilia.Dxf.Entities;

namespace IxMilia.Dxf.Blocks
{
    public class DxfBlock : IDxfHasHandle
    {
        internal const string BlockText = "BLOCK";
        internal const string EndBlockText = "ENDBLK";
        internal const string AcDbEntityText = "AcDbEntity";
        internal const string AcDbBlockBeginText = "AcDbBlockBegin";
        internal const string AcDbBlockEndText = "AcDbBlockEnd";

        private int Flags = 0;

        public uint Handle { get; set; }
        public string Layer { get; set; }
        public string Name { get; set; }
        public DxfPoint BasePoint { get; set; }
        public string XrefName { get; set; }
        public List<DxfEntity> Entities { get; private set; }
        public uint OwnerHandle { get; set; }
        public string Description { get; set; }
        public DxfXData XData { get; set; }
        public List<DxfCodePairGroup> StartExtensionDataGroups { get; private set; }
        public List<DxfCodePairGroup> EndExtensionDataGroups { get; private set; }

        public bool IsAnonymous
        {
            get { return DxfHelpers.GetFlag(Flags, 1); }
            set { DxfHelpers.SetFlag(value, ref Flags, 1); }
        }

        public bool HasAttributeDefinitions
        {
            get { return DxfHelpers.GetFlag(Flags, 2); }
            set { DxfHelpers.SetFlag(value, ref Flags, 2); }
        }

        public bool IsXref
        {
            get { return DxfHelpers.GetFlag(Flags, 4); }
            set { DxfHelpers.SetFlag(value, ref Flags, 4); }
        }

        public bool IsXrefOverlay
        {
            get { return DxfHelpers.GetFlag(Flags, 8); }
            set { DxfHelpers.SetFlag(value, ref Flags, 8); }
        }

        public bool IsExternallyDependent
        {
            get { return DxfHelpers.GetFlag(Flags, 16); }
            set { DxfHelpers.SetFlag(value, ref Flags, 16); }
        }

        public bool IsResolved
        {
            get { return DxfHelpers.GetFlag(Flags, 32); }
            set { DxfHelpers.SetFlag(value, ref Flags, 32); }
        }

        public bool IsReferencedExternally
        {
            get { return DxfHelpers.GetFlag(Flags, 64); }
            set { DxfHelpers.SetFlag(value, ref Flags, 64); }
        }

        public DxfBlock()
        {
            BasePoint = DxfPoint.Origin;
            Entities = new List<DxfEntity>();
            StartExtensionDataGroups = new List<DxfCodePairGroup>();
            EndExtensionDataGroups = new List<DxfCodePairGroup>();
        }

        internal IEnumerable<DxfCodePair> GetValuePairs(DxfAcadVersion version, bool outputHandles)
        {
            var list = new List<DxfCodePair>();
            list.Add(new DxfCodePair(0, BlockText));
            if (outputHandles)
            {
                list.Add(new DxfCodePair(5, DxfCommonConverters.UIntHandle(Handle)));
            }

            if (version >= DxfAcadVersion.R14)
            {
                foreach (var group in StartExtensionDataGroups)
                {
                    group.AddValuePairs(list, version, outputHandles);
                }
            }

            if (version >= DxfAcadVersion.R13)
            {
                list.Add(new DxfCodePair(330, DxfCommonConverters.UIntHandle(OwnerHandle)));
                list.Add(new DxfCodePair(100, AcDbEntityText));
            }

            list.Add(new DxfCodePair(8, Layer));
            if (version >= DxfAcadVersion.R13)
            {
                list.Add(new DxfCodePair(100, AcDbBlockBeginText));
            }

            list.Add(new DxfCodePair(2, Name));
            list.Add(new DxfCodePair(70, (short)Flags));
            list.Add(new DxfCodePair(10, BasePoint.X));
            list.Add(new DxfCodePair(20, BasePoint.Y));
            list.Add(new DxfCodePair(30, BasePoint.Z));
            if (version >= DxfAcadVersion.R12)
            {
                list.Add(new DxfCodePair(3, Name));
            }

            if (!string.IsNullOrEmpty(XrefName))
            {
                list.Add(new DxfCodePair(1, XrefName));
            }

            if (!string.IsNullOrEmpty(Description))
            {
                list.Add(new DxfCodePair(4, Description));
            }

            // entities in blocks never have handles
            list.AddRange(Entities.SelectMany(e => e.GetValuePairs(version, outputHandles: false)));

            list.Add(new DxfCodePair(0, EndBlockText));
            if (outputHandles)
            {
                list.Add(new DxfCodePair(5, DxfCommonConverters.UIntHandle(Handle)));
            }

            if (XData != null)
            {
                XData.AddValuePairs(list, version, outputHandles);
            }

            if (version >= DxfAcadVersion.R14)
            {
                foreach (var group in EndExtensionDataGroups)
                {
                    group.AddValuePairs(list, version, outputHandles);
                }
            }

            if (version >= DxfAcadVersion.R2000)
            {
                list.Add(new DxfCodePair(330, 0));
            }

            if (version >= DxfAcadVersion.R13)
            {
                list.Add(new DxfCodePair(100, AcDbEntityText));
                list.Add(new DxfCodePair(8, Layer));
                list.Add(new DxfCodePair(100, AcDbBlockEndText));
            }

            return list;
        }

        internal static DxfBlock FromBuffer(DxfCodePairBufferReader buffer, DxfAcadVersion version)
        {
            if (!buffer.ItemsRemain)
            {
                return null;
            }

            var block = new DxfBlock();
            var readingBlockStart = true;
            var readingBlockEnd = false;
            while (buffer.ItemsRemain)
            {
                var pair = buffer.Peek();
                if (DxfCodePair.IsSectionEnd(pair))
                {
                    // done reading blocks
                    buffer.Advance(); // swallow (0, ENDSEC)
                    break;
                }
                else if (IsBlockStart(pair))
                {
                    if (readingBlockStart || !readingBlockEnd)
                        throw new DxfReadException("Unexpected block start", pair);
                    break;
                }
                else if (IsBlockEnd(pair))
                {
                    if (!readingBlockStart) throw new DxfReadException("Unexpected block end", pair);
                    readingBlockStart = false;
                    readingBlockEnd = true;
                    buffer.Advance(); // swallow (0, ENDBLK)
                }
                else if (pair.Code == 0)
                {
                    // should be an entity
                    var entity = DxfEntity.FromBuffer(buffer);
                    Debug.Assert(entity != null);
                    if (entity != null)
                        block.Entities.Add(entity);
                }
                else
                {
                    // read value pair
                    if (readingBlockStart)
                    {
                        buffer.Advance();
                        switch (pair.Code)
                        {
                            case 1:
                                block.XrefName = pair.StringValue;
                                break;
                            case 2:
                                block.Name = pair.StringValue;
                                break;
                            case 3:
                                break;
                            case 4:
                                block.Description = pair.StringValue;
                                break;
                            case 5:
                                block.Handle = DxfCommonConverters.UIntHandle(pair.StringValue);
                                break;
                            case 8:
                                block.Layer = pair.StringValue;
                                break;
                            case 10:
                                block.BasePoint.X = pair.DoubleValue;
                                break;
                            case 20:
                                block.BasePoint.Y = pair.DoubleValue;
                                break;
                            case 30:
                                block.BasePoint.Z = pair.DoubleValue;
                                break;
                            case 70:
                                block.Flags = pair.ShortValue;
                                break;
                            case 330:
                                block.OwnerHandle = DxfCommonConverters.UIntHandle(pair.StringValue);
                                break;
                            case DxfCodePairGroup.GroupCodeNumber:
                                var groupName = DxfCodePairGroup.GetGroupName(pair.StringValue);
                                block.StartExtensionDataGroups.Add(DxfCodePairGroup.FromBuffer(buffer, groupName));
                                break;
                            case (int)DxfXDataType.ApplicationName:
                                block.XData = DxfXData.FromBuffer(buffer, pair.StringValue);
                                break;
                        }
                    }
                    else if (readingBlockEnd)
                    {
                        buffer.Advance();
                        switch (pair.Code)
                        {
                            case 5:
                                Debug.Assert(DxfCommonConverters.UIntHandle(pair.StringValue) == block.Handle);
                                break;
                            case 8:
                                Debug.Assert(version == DxfAcadVersion.R13);
                                Debug.Assert(pair.StringValue == block.Layer);
                                break;
                            case 100:
                                Debug.Assert(pair.StringValue == AcDbEntityText || pair.StringValue == AcDbBlockEndText);
                                break;
                            case DxfCodePairGroup.GroupCodeNumber:
                                var groupName = DxfCodePairGroup.GetGroupName(pair.StringValue);
                                block.EndExtensionDataGroups.Add(DxfCodePairGroup.FromBuffer(buffer, groupName));
                                break;
                        }
                    }
                    else
                    {
                        throw new DxfReadException("Unexpected pair in block", pair);
                    }
                }
            }

            return block;
        }

        private static bool IsBlockStart(DxfCodePair pair)
        {
            return pair.Code == 0 && pair.StringValue == BlockText;
        }

        private static bool IsBlockEnd(DxfCodePair pair)
        {
            return pair.Code == 0 && pair.StringValue == EndBlockText;
        }
    }
}
