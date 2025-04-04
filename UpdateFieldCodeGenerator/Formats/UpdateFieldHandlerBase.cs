using System.Reflection;
using UpdateFieldCodeGenerator.Structures;

namespace UpdateFieldCodeGenerator.Formats
{
    public abstract class UpdateFieldHandlerBase : IUpdateFieldHandler
    {
        protected TextWriter _header;
        protected TextWriter _source;
        protected Type _structureType;
        protected bool _create;
        protected bool _writeUpdateMasks;
        protected bool _isRoot;
        protected int _indent = 1;
        protected readonly IDictionary<string, List<int>> _fieldBitIndex = new Dictionary<string, List<int>>();
        protected List<int> _previousFieldCounters;
        protected int _blockGroupBit;
        protected int _blockGroupSize;
        protected int _bitCounter;
        protected int _nonArrayBitCounter;
        protected List<(string Name, bool IsSize, Func<List<FlowControlBlock>, List<FlowControlBlock>> Write)> _fieldWrites;
        protected List<string> _dynamicChangesMaskTypes;

        protected UpdateFieldHandlerBase(TextWriter source, TextWriter header)
        {
            _header = header;
            _source = source;
        }

        protected string GetIndent()
        {
            return "".PadLeft(_indent * 4);
        }

        protected void WriteControlBlocks(TextWriter output, IReadOnlyList<FlowControlBlock> flowControlBlocks, IReadOnlyList<FlowControlBlock> previousControlFlow)
        {
            var blocksMatching = true;
            for (var i = 0; i < flowControlBlocks.Count; ++i)
            {
                var currentBlock = flowControlBlocks[i];
                if (blocksMatching)
                {
                    var previousFieldBlock = previousControlFlow != null && i < previousControlFlow.Count ? previousControlFlow[i] : null;
                    if (previousFieldBlock?.Statement != currentBlock.Statement)
                    {
                        blocksMatching = false;
                        // write closing brackets
                        if (previousFieldBlock != null)
                            for (var j = previousControlFlow.Count; j > i; --j)
                                output.WriteLine($"{"".PadLeft((j + _indent - 1) * 4)}}}");
                    }
                }

                if (!blocksMatching)
                {
                    var pad = "".PadLeft((i + _indent) * 4);
                    output.WriteLine($"{pad}{currentBlock.Statement}");
                    output.WriteLine($"{pad}{{");
                }
            }

            if (blocksMatching && previousControlFlow != null && previousControlFlow.Count > flowControlBlocks.Count)
                for (var i = previousControlFlow.Count; i > flowControlBlocks.Count; --i)
                    output.WriteLine($"{"".PadLeft((i + _indent - 1) * 4)}}}");

            _indent += flowControlBlocks.Count;
        }

        public void FinishControlBlocks(TextWriter output, IReadOnlyList<FlowControlBlock> previousControlFlow)
        {
            if (previousControlFlow != null)
                for (var i = previousControlFlow.Count; i > 0; --i)
                    output.WriteLine($"{"".PadLeft((i + _indent - 1) * 4)}}}");
        }

        public virtual void BeforeStructures()
        {
        }

        public virtual void AfterStructures()
        {
        }

        public virtual void OnStructureBegin(Type structureType, ObjectType objectType, bool create, bool writeUpdateMasks)
        {
            _structureType = structureType;
            _create = create;
            _writeUpdateMasks = writeUpdateMasks;
            _isRoot = false;
            try
            {
                Program.GetObjectType(structureType);
                _isRoot = true;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            _fieldBitIndex.Clear();
            _blockGroupBit = 0;
            _blockGroupSize = structureType.GetCustomAttribute<HasChangesMaskAttribute>()?.BlockGroupSize ?? 32;
            _bitCounter = HasNonArrayFields(structureType) && /*CountFields(structureType, _ => true) > 1 &&*/ _blockGroupSize > 0 ? 0 : -1;
            _nonArrayBitCounter = 0;
            _fieldWrites = new List<(string Name, bool IsSize, Func<List<FlowControlBlock>, List<FlowControlBlock>> Write)>();
            _dynamicChangesMaskTypes = new List<string>();
        }

        public abstract void OnStructureEnd(bool needsFlush, bool forceMaskMask);

        public abstract IReadOnlyList<FlowControlBlock> OnField(string name, UpdateField updateField, IReadOnlyList<FlowControlBlock> previousBlock);
        public abstract IReadOnlyList<FlowControlBlock> OnDynamicFieldSizeCreate(string name, UpdateField updateField, IReadOnlyList<FlowControlBlock> previousControlFlow);
        public abstract IReadOnlyList<FlowControlBlock> OnDynamicFieldSizeUpdate(string name, UpdateField updateField, IReadOnlyList<FlowControlBlock> previousControlFlow);
        public abstract IReadOnlyList<FlowControlBlock> OnOptionalFieldInitCreate(string name, UpdateField updateField, IReadOnlyList<FlowControlBlock> previousControlFlow);
        public abstract IReadOnlyList<FlowControlBlock> OnOptionalFieldInitUpdate(string name, UpdateField updateField, IReadOnlyList<FlowControlBlock> previousControlFlow);

        public abstract void FinishControlBlocks(IReadOnlyList<FlowControlBlock> previousControlFlow, string tag);

        public abstract void FinishBitPack(string tag);

        protected static bool HasNonArrayFields(Type type)
        {
            return CountFields(type, field => !field.Type.IsArray) > 0;
        }

        protected static int CountFields(Type type, Func<UpdateField, bool> pred)
        {
            return type.GetFields(BindingFlags.Static | BindingFlags.Public)
                .Where(field => typeof(UpdateField).IsAssignableFrom(field.FieldType))
                .Select(field => field.GetValue(null) as UpdateField)
                .Where(pred)
                .Count();
        }

        protected virtual string RenameType(Type type)
        {
            return type.Name;
        }

        protected abstract string RenameField(string name);

        protected void PostProcessFieldWrites()
        {
            Action<string, bool, string, bool> moveFieldBeforeField = (fieldToMove, fieldIsSize, where, whereIsSize) =>
            {
                fieldToMove = RenameField(fieldToMove);
                where = RenameField(where);
                var movedFieldIndex = _fieldWrites.FindIndex(fieldWrite => fieldWrite.Name == fieldToMove && fieldWrite.IsSize == fieldIsSize);
                var whereFieldIndex = _fieldWrites.FindIndex(fieldWrite => fieldWrite.Name == where && fieldWrite.IsSize == whereIsSize);
                if (movedFieldIndex != -1 && whereFieldIndex != -1)
                {
                    // move to just-before-last field
                    var movedField = _fieldWrites[movedFieldIndex];
                    _fieldWrites.RemoveAt(movedFieldIndex);
                    _fieldWrites.Insert(whereFieldIndex < movedFieldIndex ? whereFieldIndex : whereFieldIndex - 1, movedField);
                }
            };

            Action<string> moveFieldToEnd = (fieldToMove) =>
            {
                fieldToMove = RenameField(fieldToMove);
                var movedFieldIndex = _fieldWrites.FindIndex(fieldWrite => fieldWrite.Name == fieldToMove && !fieldWrite.IsSize);
                if (movedFieldIndex != -1)
                {
                    // move to just-before-last field
                    var movedField = _fieldWrites[movedFieldIndex];
                    _fieldWrites.RemoveAt(movedFieldIndex);
                    _fieldWrites.Insert(_fieldWrites.Count - 1, movedField);
                }
            };

            if (_structureType == typeof(CGItemData))
            {
                if (!_create)
                    moveFieldBeforeField("m_modifiers", false, "m_spellCharges", false);
            }
            else if (_structureType == typeof(CGPlayerData))
            {
                if (_create)
                {
                    moveFieldToEnd("declinedNames");
                    moveFieldBeforeField("dungeonScore", false, "declinedNames", false);
                    moveFieldBeforeField("name", false, "declinedNames", false);
                }
                else
                {
                    FinishBitPack("FinishBitPack_afterDeclinedNamesBit");

                    moveFieldBeforeField("name{0}size()", false, "partyType", false);
                    moveFieldBeforeField("declinedNames.has_value()", false, "partyType", false);
                    moveFieldBeforeField("FinishBitPack_afterDeclinedNamesBit", false, "partyType", false);
                    moveFieldBeforeField("dungeonScore", false, "partyType", false);
                    moveFieldBeforeField("name", false, "partyType", false);
                    moveFieldBeforeField("declinedNames", false, "partyType", false);
                }
            }
            else if (_structureType == typeof(JamMirrorDeclinedNames_C))
            {
                FinishControlBlocks(null, "SplitBits");
                FinishBitPack("FinishBitPack_SplitBits");
                moveFieldBeforeField("SplitBits", false, "m_name", false);
                moveFieldBeforeField("FinishBitPack_SplitBits", false, "m_name", false);
            }
            else if (_structureType == typeof(CGActivePlayerData))
            {
                moveFieldBeforeField("researchSites", true, "dailyQuestsCompleted", true);
                moveFieldBeforeField("researchSiteProgress", true, "dailyQuestsCompleted", true);
                moveFieldBeforeField("research", true, "dailyQuestsCompleted", true);
                moveFieldBeforeField("researchSites", false, "dailyQuestsCompleted", true);
                moveFieldBeforeField("researchSiteProgress", false, "dailyQuestsCompleted", true);
                moveFieldBeforeField("research", false, "dailyQuestsCompleted", true);

                if (_create)
                {
                    moveFieldBeforeField("petStable.has_value()", false, "sortBagsRightToLeft", false);
                    moveFieldBeforeField("accountBankTabSettings", true, "sortBagsRightToLeft", false);
                    moveFieldBeforeField("researchHistory", false, "sortBagsRightToLeft", false);
                    moveFieldBeforeField("frozenPerksVendorItem", false, "sortBagsRightToLeft", false);
                    moveFieldBeforeField("sortBagsRightToLeft", false, "petStable.has_value()", false);
                    moveFieldBeforeField("insertItemsLeftToRight", false, "petStable.has_value()", false);
                    moveFieldBeforeField("petStable", false, "accountBankTabSettings", false);
                }
                else
                {
                    FinishControlBlocks(null, string.Empty);
                    FinishBitPack("FinishBitPack_afterResearch");

                    var finishBitPack = _fieldWrites.GetRange(_fieldWrites.Count - 2, 2);
                    _fieldWrites.RemoveRange(_fieldWrites.Count - 2, 2);

                    var researchIndex = _fieldWrites.FindIndex(fieldWrite => fieldWrite.Name == RenameField("research") && !fieldWrite.IsSize);
                    _fieldWrites.InsertRange(researchIndex + 1, finishBitPack);

                    moveFieldBeforeField("accountBankTabSettings", true, "characterDataElements", false);
                    moveFieldBeforeField("researchHistory", false, "petStable", false);
                    moveFieldBeforeField("frozenPerksVendorItem", false, "petStable", false);
                    moveFieldBeforeField("numStableSlots", false, "petStable.has_value()", false);

                    moveFieldToEnd("pvpInfo");

                    //FinishControlBlocks(null, string.Empty);
                    //FinishBitPack("FinishBitPack_afterOptionalBit");
                    //var finishBitPackAfterOptionalBit = _fieldWrites.GetRange(_fieldWrites.Count - 2, 2);
                    //_fieldWrites.RemoveRange(_fieldWrites.Count - 2, 2);
                    //
                    //var researchHistoryIndex = _fieldWrites.FindIndex(fieldWrite => fieldWrite.Name == RenameField("researchHistory") && !fieldWrite.IsSize);
                    //_fieldWrites.InsertRange(researchHistoryIndex, finishBitPackAfterOptionalBit);
                    //
                    //moveFieldBeforeField("petStable.has_value()", false, "researchHistory", false);
                }

                //    if (_create)
                //    {
                //        moveFieldToEnd("delveData");
                //        moveFieldBeforeField("walkInData", false, "delveData", false);
                //        moveFieldBeforeField("accountBankTabSettings", false, "walkInData", false);
                //        moveFieldBeforeField("petStable", false, "accountBankTabSettings", false);
                //
                //        moveFieldBeforeField("dungeonScore", false, "pvpInfo", false);
                //        moveFieldBeforeField("characterDataElements", false, "pvpInfo", false);
                //        moveFieldBeforeField("accountDataElements", false, "pvpInfo", false);
                //    }
                //    else
                //    {
                //        moveFieldBeforeField("accountBankTabSettings", true, "characterDataElements", false);
                //        moveFieldBeforeField("accountBankTabSettings", false, "farsightObject", false);
                //
                //        moveFieldBeforeField("delveData", false, "invSlots", false);
                //        moveFieldBeforeField("walkInData", false, "delveData", false);
                //        moveFieldBeforeField("petStable", false, "walkInData", false);
                //        moveFieldBeforeField("dungeonScore", false, "petStable", false);
                //
                //        FinishControlBlocks(null, string.Empty);
                //        FinishBitPack("FinishBitPack_afterResearch");
                //
                //        var finishBitPack = _fieldWrites.GetRange(_fieldWrites.Count - 2, 2);
                //        _fieldWrites.RemoveRange(_fieldWrites.Count - 2, 2);
                //
                //        var researchIndex = _fieldWrites.FindIndex(fieldWrite => fieldWrite.Name == RenameField("research") && !fieldWrite.IsSize);
                //        _fieldWrites.InsertRange(researchIndex + 1, finishBitPack);
                //    }
                //
                //    moveFieldBeforeField("field_1410", false, "dungeonScore", false);
                //    moveFieldBeforeField("frozenPerksVendorItem", false, "field_1410", false);
                //    moveFieldBeforeField("questSession", false, "frozenPerksVendorItem", false);
                //    moveFieldBeforeField("researchHistory", false, "questSession", false);
                //    moveFieldBeforeField("delveData.has_value()", false, "researchHistory", false);
                //    moveFieldBeforeField("walkInData.has_value()", false, "delveData.has_value()", false);
                //
                //    if (_create)
                //    {
                //        moveFieldBeforeField("accountBankTabSettings", true, "walkInData.has_value()", false);
                //        moveFieldBeforeField("petStable.has_value()", false, "accountBankTabSettings", true);
                //    }
                //    else
                //    {
                //        moveFieldBeforeField("petStable.has_value()", false, "walkInData.has_value()", false);
                //    }
                //
                //    moveFieldBeforeField("questSession.has_value()", false, "petStable.has_value()", false);
                //
                //    FinishControlBlocks(null, string.Empty);
                //    FinishBitPack("FinishBitPack_afterOptionalBit");
                //    var finishBitPackAfterOptionalBit = _fieldWrites.GetRange(_fieldWrites.Count - 2, 2);
                //    _fieldWrites.RemoveRange(_fieldWrites.Count - 2, 2);
                //
                //    var researchHistoryIndex = _fieldWrites.FindIndex(fieldWrite => fieldWrite.Name == RenameField("researchHistory") && !fieldWrite.IsSize);
                //    _fieldWrites.InsertRange(researchHistoryIndex, finishBitPackAfterOptionalBit);
            }
            else if (_structureType == typeof(JamMirrorPlayerDataElement_C))
            {
                if (_create)
                    moveFieldBeforeField("m_type", false, "m_floatValue", false);
            }
            else if (_structureType == typeof(JamMirrorTraitConfig_C))
            {
                moveFieldToEnd("m_name");
                moveFieldBeforeField("m_name{0}size()", false, "m_name", false);
                if (_create)
                    moveFieldBeforeField("m_subTrees", false, "m_name", false);
            }
            else if (_structureType == typeof(JamMirrorTraitSubTreeCache_C))
            {
                if (!_create)
                    moveFieldBeforeField("m_traitSubTreeID", false, "m_entries{0}size()", false);

            }
            else if (_structureType == typeof(JamMirrorStablePetInfo_C))
            {
                if (!_create)
                {
                    moveFieldToEnd("m_name");
                    moveFieldBeforeField("m_name{0}size()", false, "m_name", false);
                }
            }
            else if (_structureType == typeof(JamMirrorBankTabSettings_C))
            {
                moveFieldBeforeField("m_depositFlags", false, "m_name", false);
            }
            else if (_structureType == typeof(JamMirrorConversationActor_C))
            {
                if (_create)
                    moveFieldBeforeField("m_type", false, "m_id", false);
            }
            else if (_structureType == typeof(CGAreaTriggerData))
            {
                if (_create)
                {
                    moveFieldBeforeField("m_overrideScaleCurve", false, "m_caster", false);
                }
                else
                {
                    moveFieldBeforeField("m_extraScaleCurve", false, "m_visualAnim", false);
                    moveFieldBeforeField("m_overrideMoveCurveX", false, "m_visualAnim", false);
                    moveFieldBeforeField("m_overrideMoveCurveY", false, "m_visualAnim", false);
                    moveFieldBeforeField("m_overrideMoveCurveZ", false, "m_visualAnim", false);
                }
            }
            //else if (_structureType == typeof(CGConversationData))
            //{
            //    if (_create)
            //    {
            //        var dontPlayBroadcastTextSoundsIndex = _fieldWrites.FindIndex(fw => fw.Name == RenameField("m_dontPlayBroadcastTextSounds"));
            //        var actorsSizeIndex = _fieldWrites.FindIndex(fw => fw.Name == RenameField("m_actors") && fw.IsSize);
            //        if (actorsSizeIndex != -1)
            //        {
            //            // move to just-before-end (end is a write for closing all brackets)
            //            var dontPlayBroadcastTextSounds = _fieldWrites[dontPlayBroadcastTextSoundsIndex];
            //            _fieldWrites.RemoveAt(dontPlayBroadcastTextSoundsIndex);
            //            _fieldWrites.Insert(actorsSizeIndex, dontPlayBroadcastTextSounds);
            //        }
            //    }
            //}
        }

        protected void RegisterDynamicChangesMaskFieldType(Type fieldType)
        {
            if (_dynamicChangesMaskTypes.Contains(fieldType.Name))
                return;

            _dynamicChangesMaskTypes.Add(fieldType.Name);
        }

        public void Dispose()
        {
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
            if (_header != null)
            {
                _header.Dispose();
                _header = null;
            }
        }
    }
}
