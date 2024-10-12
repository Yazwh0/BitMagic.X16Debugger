using BitMagic.X16Emulator;
using BitMagic.X16Emulator.Snapshot;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Text;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger;

internal class VariableManager
{
    private readonly Dictionary<int, IVariableItem> _variablesById = new();
    private readonly Dictionary<string, object> _variableObjectTree = new();

    private readonly IdManager _idManager;
    private readonly Emulator _emulator;
    private readonly ScopeManager _scopeManager;
    private readonly PaletteManager _paletteManager;
    private readonly SpriteManager _spriteManager;
    private readonly StackManager _stackManager;
    private readonly PsgManager _psgManager;
    private ExpressionManager? _expressionManager;

    public VariableManager(IdManager idManager, Emulator emulator, ScopeManager scopeManager, PaletteManager paletteManager,
        SpriteManager spriteManager, StackManager stackManager, PsgManager psgManager)
    {
        _idManager = idManager;
        _emulator = emulator;
        _scopeManager = scopeManager;
        _paletteManager = paletteManager;
        _spriteManager = spriteManager;
        _stackManager = stackManager;
        _psgManager = psgManager;
        SetupVariables();
    }

    public void SetExpressionManager(ExpressionManager expressionManager)
    {
        _expressionManager = expressionManager;
    }

    public IDictionary<string, object> ObjectTree => _variableObjectTree;

    public IVariableItem? Get(int id)
    {
        if (_variablesById.ContainsKey(id))
            return _variablesById[id];

        return null;
    }

    public VariableChildren? GetChildren(int id)
    {
        if (_variablesById.ContainsKey(id))
            return _variablesById[id] as VariableChildren;

        return null;
    }

    public VariableIndex? GetIndex(int id)
    {
        if (_variablesById.ContainsKey(id))
            return _variablesById[id] as VariableIndex;

        return null;
    }

    public VariableChildren Register(VariableChildren variable)
    {
        variable.Id = _idManager.GetId();
        _variablesById.Add(variable.Id, variable);
        return variable;
    }

    public VariableIndex Register(VariableIndex variable)
    {
        variable.Id = _idManager.GetId();
        _variablesById.Add(variable.Id, variable);
        return variable;
    }

    private ScopeWrapper GetNewScope(string name)
    {
        var key = name.Replace(" ", "");

        var toReturn = new ScopeWrapper(_scopeManager.GetScope(name, false));

        if (_variableObjectTree.ContainsKey(key))
            _variableObjectTree[key] = toReturn.ObjectTree;
        else
            _variableObjectTree.Add(key, toReturn.ObjectTree);

        return toReturn;
    }

    private void AddLocalScope(string name)
    {
        var map = _scopeManager.CreateLocalsScope(name);
        _variableObjectTree.Add(name, map);
    }

    /// <summary>
    /// Set the current scope for the variables
    /// </summary>
    /// <param name="id"></param>
    public void SetScope(StackFrameState? state)
    {
        if (state == null)
        {
            // Do we clear something?
        }
        _scopeManager.LocalScope?.SetLocalScope(state, _emulator, _expressionManager);
    }

    public void SetChanges(SnapshotResult changes)
    {
        var scope = GetNewScope("Changes");

        scope.Clear();

        var timing = new List<ISnapshotChange>();
        var register = new List<ISnapshotChange>();
        var flags = new List<ISnapshotChange>();
        var ram = new List<ISnapshotChange>();
        var bankedRam = new List<ISnapshotChange>();
        var vram = new List<ISnapshotChange>();
        var nvram = new List<ISnapshotChange>();

        foreach (var change in changes.Changes)
        {
            (change switch
            {
                RegisterChange _ => register,
                FlagChange _ => flags,
                MemoryChange m => m.MemoryArea switch
                {
                    MemoryAreas.Ram => ram,
                    MemoryAreas.NVram => nvram,
                    MemoryAreas.BankedRam => bankedRam,
                    MemoryAreas.Vram => vram,
                    _ => throw new Exception($"Unknwn area {m.MemoryArea}"),
                },
                MemoryRangeChange m => m.MemoryArea switch
                {
                    MemoryAreas.Ram => ram,
                    MemoryAreas.NVram => nvram,
                    MemoryAreas.BankedRam => bankedRam,
                    MemoryAreas.Vram => vram,
                    _ => throw new Exception($"Unknwn area {m.MemoryArea}"),
                },
                ValueChange v => v.Name switch
                {
                    "Clock" => timing,
                    _ => throw new Exception($"Unknown ValueChange {v.Name}")
                },
                _ => throw new Exception("Unknown change")
            }).Add(change);
        }

        // add cpu clock as top level
        var clock = timing.FirstOrDefault() as ValueChange;
        if (clock != null)
            scope.AddVariable(new VariableMap("Clock", "int", () => (clock.NewValue - clock.OriginalValue).ToString()));

        AddSnapshot("Registers", scope, register);
        AddSnapshot("Flags", scope, flags);
        AddSnapshot("Ram", scope, ram);
        AddSnapshot("BankedRam", scope, bankedRam);
        AddSnapshot("VRam", scope, vram);
        AddSnapshot("NVRam", scope, nvram);
    }

    private void AddSnapshot(string name, ScopeWrapper scope, List<ISnapshotChange> changes)
    {
        if (!changes.Any())
            return;

        var count = changes.Count;

        scope.AddVariable(Register(
            new VariableChildren(
                name, () => $"{count} Change{(count == 1 ? "" : "s")}",
                changes.Select(i => new VariableMap(i.DisplayName, "string", () => i.NewValue)).ToArray()
            )));
    }

    public void SetupVariables()
    {
        var scope = GetNewScope("CPU");

        scope.AddVariable(
            Register(
                new VariableChildren("Flags",
                () => $"[{(_emulator.Negative ? "N" : " ")}{(_emulator.Overflow ? "V" : " ")} {(_emulator.BreakFlag ? "B" : " ")}{(_emulator.Decimal ? "D" : " ")}{(_emulator.InterruptDisable ? "I" : " ")}{(_emulator.Zero ? "Z" : " ")}{(_emulator.Carry ? "C" : " ")}]",
                new[] {
                    new VariableMap("Negative", "bool", () => _emulator.Negative, attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Overflow", "bool", () => _emulator.Overflow, attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Break", "bool", () => _emulator.BreakFlag, attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Decimal", "bool", () => _emulator.Decimal, attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Interupt", "bool", () => _emulator.InterruptDisable, attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Zero", "bool", () => _emulator.Zero, attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Carry", "bool", () => _emulator.Carry, attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                })));
        scope.AddVariable(new VariableMap("A", "byte", () => _emulator.A, () => _emulator.A));
        scope.AddVariable(new VariableMap("X", "byte", () => _emulator.X, () => _emulator.X));
        scope.AddVariable(new VariableMap("Y", "byte", () => _emulator.Y, () => _emulator.Y));
        scope.AddVariable(new VariableMap("PC", "ushort", () => _emulator.Pc, () => _emulator.Pc));
        scope.AddVariable(new VariableMap("SP", "byte", () => $"0x{_emulator.StackPointer:X3}", () => _emulator.StackPointer));

        scope.AddVariable(new VariableMap("Ram Bank", "byte", () => _emulator.Memory[0], () => _emulator.Memory[0]));
        scope.AddVariable(new VariableMap("Rom Bank (Act)", "int", () => (byte)_emulator.RomBankAct, () => (byte)_emulator.RomBankAct));
        scope.AddVariable(new VariableMap("Rom Bank (Memory)", "byte", () => _emulator.Memory[1], () => _emulator.Memory[1]));
        scope.AddVariable(new VariableMemory("Ram", () => "CPU Visible Ram", "main", () => _emulator.Memory.ToArray()));
        scope.AddVariable(Register(new VariableChildren("Ram Banks", () => "256 Banks", GetRamBanks().ToArray())));
        scope.AddVariable(Register(new VariableChildren("Rom Banks", () => "256 Banks", GetRomBanks().ToArray())));
        scope.AddVariable(Register(new VariableIndex("Stack", _stackManager.GetStack)));
        scope.AddVariable(Register(new VariableChildren("Interrupt", () => ((_emulator.State.Interrupt_Hit & _emulator.State.Interrupt_Mask) != 0).ToString(), "bool", new[] {
                new VariableMap("Vsync ISR", "bool", () => ((_emulator.Memory[0x9F26] & 0b0001) & (_emulator.Memory[0x9F27] & 0b0001)) != 0),
                new VariableMap("Vsync Int", "bool", () => (_emulator.State.Interrupt_Hit & (uint)InterruptSource.Vsync) != 0),
                new VariableMap("Vsync Msk", "bool", () => (_emulator.State.Interrupt_Mask & (uint)InterruptSource.Vsync) != 0),
                new VariableMap("Line ISR", "bool", () => ((_emulator.Memory[0x9F26] & 0b0010) & (_emulator.Memory[0x9F27] & 0b0010)) != 0),
                new VariableMap("Line Int", "bool", () => (_emulator.State.Interrupt_Hit & (uint)InterruptSource.Line) != 0),
                new VariableMap("Line Msk", "bool", () => (_emulator.State.Interrupt_Mask & (uint)InterruptSource.Line) != 0),
                new VariableMap("SpCol ISR", "bool", () => ((_emulator.Memory[0x9F26] & 0b0100) & (_emulator.Memory[0x9F27] & 0b0100)) != 0),
                new VariableMap("SpCol Int", "bool", () => (_emulator.State.Interrupt_Hit & (uint)InterruptSource.Spcol) != 0),
                new VariableMap("SpCol Msk", "bool", () => (_emulator.State.Interrupt_Mask & (uint)InterruptSource.Spcol) != 0),
                new VariableMap("Aflow ISR", "bool", () => ((_emulator.Memory[0x9F26] & 0b1000) & (_emulator.Memory[0x9F27] & 0b1000)) != 0),
                new VariableMap("Aflow Int", "bool", () => (_emulator.State.Interrupt_Hit & (uint)InterruptSource.Aflow) != 0),
                new VariableMap("Aflow Msk", "bool", () => (_emulator.State.Interrupt_Mask & (uint)InterruptSource.Aflow) != 0),
                new VariableMap("Via Int", "bool", () => _emulator.Via.Interrupt, () => _emulator.Via.Interrupt),
                new VariableMap("Via Msk", "bool", () => (_emulator.State.Interrupt_Mask & (uint)InterruptSource.Via) != 0),
                new VariableMap("YM", "bool", () => _emulator.State.Ym_Interrupt != 0),
                new VariableMap("YM Int", "bool", () => (_emulator.State.Interrupt_Hit & (uint)InterruptSource.Ym) != 0),
                new VariableMap("YM Msk", "bool", () => (_emulator.State.Interrupt_Mask & (uint)InterruptSource.Ym) != 0),
        })));

        scope = GetNewScope("VERA");

        scope.AddVariable(
            Register(
                new VariableChildren("Data 0", () => $"0x{_emulator.Memory[0x9F23]:X2}", "int",
                new[] {
                    new VariableMap("Address", "uint", () => $"0x{_emulator.Vera.Data0_Address:X5}", () => _emulator.Vera.Data0_Address),
                    new VariableMap("Step", "byte", () => $"{_emulator.Vera.Data0_Step}", () => _emulator.Vera.Data0_Step)
                }
            )));

        scope.AddVariable(
            Register(
                new VariableChildren("Data 1", () => $"0x{_emulator.Memory[0x9F24]:X2}", "int",
                new[] {
                    new VariableMap("Address", "uint", () => $"0x{_emulator.Vera.Data1_Address:X5}", () => _emulator.Vera.Data1_Address),
                    new VariableMap("Step", "byte", () => $"{_emulator.Vera.Data1_Step}", () => _emulator.Vera.Data1_Step)
                }
            )));

        scope.AddVariable(
            Register(
                new VariableChildren("Layer 0", () => _emulator.Vera.Layer0Enable ?
                    (_emulator.Vera.Layer0_BitMapMode ? $"{GetColourDepth(_emulator.Vera.Layer0_ColourDepth):0}bpp Bitmap" : $"{GetColourDepth(_emulator.Vera.Layer0_ColourDepth):0}bpp{GetT256C(_emulator.Vera.Layer0_T256C)}Tiles") :
                    "Disabled",
                new[] {
                    new VariableMap("Map Address", "uint", () => $"0x{_emulator.Vera.Layer0_MapAddress:X5}", () => _emulator.Vera.Layer0_MapAddress),
                    new VariableMap("Tile Address", "uint", () => $"0x{_emulator.Vera.Layer0_TileAddress:X5}", () => _emulator.Vera.Layer0_TileAddress),
                    new VariableMap("HScroll", "uint", () => $"0x{_emulator.Vera.Layer0_HScroll:X2}", () => _emulator.Vera.Layer0_HScroll),
                    new VariableMap("VScroll", "uint", () => $"0x{_emulator.Vera.Layer0_VScroll:X2}", () => _emulator.Vera.Layer0_VScroll),
                    new VariableMap("Tile Width", "uint", () => $"{(_emulator.Vera.Layer0_TileWidth == 0 ? 8 : 16)}", () => (_emulator.Vera.Layer0_TileWidth == 0 ? 8 : 16)),
                    new VariableMap("Tile Height", "uint", () => $"{(_emulator.Vera.Layer0_TileHeight == 0 ? 8 : 16)}", () =>(_emulator.Vera.Layer0_TileHeight == 0 ? 8 : 16)),
                    new VariableMap("Map Width", "uint", () => $"{GetMapSize(_emulator.Vera.Layer0_MapWidth)}", () => GetMapSize(_emulator.Vera.Layer0_MapWidth)),
                    new VariableMap("Map Height", "uint", () => $"{GetMapSize(_emulator.Vera.Layer0_MapHeight)}", () => GetMapSize(_emulator.Vera.Layer0_MapHeight)),
                }
            )));

        scope.AddVariable(
            Register(
                new VariableChildren("Layer 1", () => _emulator.Vera.Layer1Enable ?
                    (_emulator.Vera.Layer1_BitMapMode ? $"{GetColourDepth(_emulator.Vera.Layer1_ColourDepth):0}bpp Bitmap" : $"{GetColourDepth(_emulator.Vera.Layer1_ColourDepth):0}bpp{GetT256C(_emulator.Vera.Layer1_T256C)}Tiles") :
                    "Disabled",
                new[] {
                    new VariableMap("Map Address", "uint", () => $"0x{_emulator.Vera.Layer1_MapAddress:X5}", () => _emulator.Vera.Layer1_MapAddress),
                    new VariableMap("Tile Address", "uint", () => $"0x{_emulator.Vera.Layer1_TileAddress:X5}", () => _emulator.Vera.Layer1_TileAddress),
                    new VariableMap("HScroll", "uint", () => $"0x{_emulator.Vera.Layer1_HScroll:X2}", () => _emulator.Vera.Layer1_HScroll),
                    new VariableMap("VScroll", "uint", () => $"0x{_emulator.Vera.Layer1_VScroll:X2}", () => _emulator.Vera.Layer1_VScroll),
                    new VariableMap("Tile Width", "uint", () => $"{(_emulator.Vera.Layer1_TileWidth == 0 ? 8 : 16)}", () =>(_emulator.Vera.Layer1_TileWidth == 0 ? 8 : 16)),
                    new VariableMap("Tile Height", "uint", () => $"{(_emulator.Vera.Layer1_TileHeight == 0 ? 8 : 16)}", () =>(_emulator.Vera.Layer1_TileHeight == 0 ? 8 : 16)),
                    new VariableMap("Map Width", "uint", () => $"{GetMapSize(_emulator.Vera.Layer1_MapWidth)}", () => GetMapSize(_emulator.Vera.Layer1_MapWidth)),
                    new VariableMap("Map Height", "uint", () => $"{GetMapSize(_emulator.Vera.Layer1_MapHeight)}", () => GetMapSize(_emulator.Vera.Layer1_MapHeight)),
                }
            )));

        scope.AddVariable(new VariableMap("DcSel", "uint", () => $"{_emulator.Vera.DcSel}", () => _emulator.Vera.DcSel));
        scope.AddVariable(new VariableMap("Output", "uint", () => $"{_emulator.Vera.VideoOutput}", () => _emulator.Vera.VideoOutput));
        scope.AddVariable(new VariableMap("DC HStart", "uint", () => $"{_emulator.Vera.Dc_HStart}", () => _emulator.Vera.Dc_HStart));
        scope.AddVariable(new VariableMap("DC VStart", "uint", () => $"{_emulator.Vera.Dc_VStart}", () => _emulator.Vera.Dc_VStart));
        scope.AddVariable(new VariableMap("DC HStop", "uint", () => $"{_emulator.Vera.Dc_HStop}", () => _emulator.Vera.Dc_HStop));
        scope.AddVariable(new VariableMap("DC VStop", "uint", () => $"{_emulator.Vera.Dc_VStop}", () => _emulator.Vera.Dc_VStop));

        scope.AddVariable(
            Register(
                new VariableChildren("Sprites", () => _emulator.Vera.SpriteEnable ?
                    "Enabled" :
                    "Disabled",
                new IVariableItem[] {
                    Register(
                            new VariableIndex("Sprites", _spriteManager.GetFunction)
                        )
                    ,
                    Register(
                        new VariableChildren("Renderer", () => $"",
                        new[] {
                            new VariableMap("Render Mode", "uint", () => $"{_emulator.Vera.Sprite_Render_Mode}", () => _emulator.Vera.Sprite_Render_Mode),
                            new VariableMap("VRAM Wait", "uint", () => $"{_emulator.Vera.Sprite_Wait}", () => _emulator.Vera.Sprite_Wait),
                            new VariableMap("Sprite Index", "uint", () => $"{_emulator.Vera.Sprite_Position}", () => _emulator.Vera.Sprite_Position),
                            new VariableMap("Snapped X", "uint", () => $"{_emulator.Vera.Sprite_X}", () => _emulator.Vera.Sprite_X),
                            new VariableMap("Snapped Y", "uint", () => $"{_emulator.Vera.Sprite_Y}", () => _emulator.Vera.Sprite_Y),
                            new VariableMap("Snapped Width", "uint", () => $"{_emulator.Vera.Sprite_Width}", () => _emulator.Vera.Sprite_Width),
                            new VariableMap("Depth", "uint", () => $"{_emulator.Vera.Sprite_Depth}", () => _emulator.Vera.Sprite_Depth),
                            new VariableMap("Colission mask", "uint", () => $"0b{Convert.ToString(_emulator.Vera.Sprite_CollisionMask, 2).PadLeft(4, '0')}", () => _emulator.Vera.Sprite_CollisionMask),
                            })
                        )
                    }
                )));

        _spriteManager.Register(this);

        scope.AddVariable(
                    Register(
                            new VariableIndex("Palette", _paletteManager.GetFunction)
                        )
                );

        scope.AddVariable(
                Register(
                    new VariableChildren("IO Area", () => "0x9f20 -> 34",
                    new[]
                    {
                        new VariableMap("IO 0x9f20", "ushort", () => $"0x{_emulator.Memory[0x9f20]:X2}", () => _emulator.Memory[0x9f20]),
                        new VariableMap("IO 0x9f21", "ushort", () => $"0x{_emulator.Memory[0x9f21]:X2}", () => _emulator.Memory[0x9f21]),
                        new VariableMap("IO 0x9f22", "ushort", () => $"0x{_emulator.Memory[0x9f22]:X2}", () => _emulator.Memory[0x9f22]),
                        new VariableMap("IO 0x9f23", "ushort", () => $"0x{_emulator.Memory[0x9f23]:X2}", () => _emulator.Memory[0x9f23]),
                        new VariableMap("IO 0x9f24", "ushort", () => $"0x{_emulator.Memory[0x9f24]:X2}", () => _emulator.Memory[0x9f24]),
                        new VariableMap("IO 0x9f25", "ushort", () => $"0x{_emulator.Memory[0x9f25]:X2}", () => _emulator.Memory[0x9f25]),
                        new VariableMap("IO 0x9f26", "ushort", () => $"0x{_emulator.Memory[0x9f26]:X2}", () => _emulator.Memory[0x9f26]),
                        new VariableMap("IO 0x9f27", "ushort", () => $"0x{_emulator.Memory[0x9f27]:X2}", () => _emulator.Memory[0x9f27]),
                        new VariableMap("IO 0x9f28", "ushort", () => $"0x{_emulator.Memory[0x9f28]:X2}", () => _emulator.Memory[0x9f28]),
                        new VariableMap("IO 0x9f29", "ushort", () => $"0x{_emulator.Memory[0x9f29]:X2}", () => _emulator.Memory[0x9f29]),
                        new VariableMap("IO 0x9f2a", "ushort", () => $"0x{_emulator.Memory[0x9f2a]:X2}", () => _emulator.Memory[0x9f2a]),
                        new VariableMap("IO 0x9f2b", "ushort", () => $"0x{_emulator.Memory[0x9f2b]:X2}", () => _emulator.Memory[0x9f2b]),
                        new VariableMap("IO 0x9f2c", "ushort", () => $"0x{_emulator.Memory[0x9f2c]:X2}", () => _emulator.Memory[0x9f2c]),
                        new VariableMap("IO 0x9f2d", "ushort", () => $"0x{_emulator.Memory[0x9f2d]:X2}", () => _emulator.Memory[0x9f2d]),
                        new VariableMap("IO 0x9f2e", "ushort", () => $"0x{_emulator.Memory[0x9f2e]:X2}", () => _emulator.Memory[0x9f2e]),
                        new VariableMap("IO 0x9f2f", "ushort", () => $"0x{_emulator.Memory[0x9f2f]:X2}", () => _emulator.Memory[0x9f2f]),
                        new VariableMap("IO 0x9f30", "ushort", () => $"0x{_emulator.Memory[0x9f30]:X2}", () => _emulator.Memory[0x9f30]),
                        new VariableMap("IO 0x9f31", "ushort", () => $"0x{_emulator.Memory[0x9f31]:X2}", () => _emulator.Memory[0x9f31]),
                        new VariableMap("IO 0x9f32", "ushort", () => $"0x{_emulator.Memory[0x9f32]:X2}", () => _emulator.Memory[0x9f32]),
                        new VariableMap("IO 0x9f33", "ushort", () => $"0x{_emulator.Memory[0x9f33]:X2}", () => _emulator.Memory[0x9f33]),
                        new VariableMap("IO 0x9f34", "ushort", () => $"0x{_emulator.Memory[0x9f34]:X2}", () => _emulator.Memory[0x9f34]),
                        new VariableMap("IO 0x9f35", "ushort", () => $"0x{_emulator.Memory[0x9f35]:X2}", () => _emulator.Memory[0x9f35]),
                        new VariableMap("IO 0x9f36", "ushort", () => $"0x{_emulator.Memory[0x9f36]:X2}", () => _emulator.Memory[0x9f36]),
                        new VariableMap("IO 0x9f37", "ushort", () => $"0x{_emulator.Memory[0x9f37]:X2}", () => _emulator.Memory[0x9f37]),
                        new VariableMap("IO 0x9f38", "ushort", () => $"0x{_emulator.Memory[0x9f38]:X2}", () => _emulator.Memory[0x9f38]),
                        new VariableMap("IO 0x9f39", "ushort", () => $"0x{_emulator.Memory[0x9f39]:X2}", () => _emulator.Memory[0x9f39]),
                        new VariableMap("IO 0x9f3a", "ushort", () => $"0x{_emulator.Memory[0x9f3a]:X2}", () => _emulator.Memory[0x9f3a]),
                        new VariableMap("IO 0x9f3b", "ushort", () => $"0x{_emulator.Memory[0x9f3b]:X2}", () => _emulator.Memory[0x9f3b]),
                        new VariableMap("IO 0x9f3c", "ushort", () => $"0x{_emulator.Memory[0x9f3c]:X2}", () => _emulator.Memory[0x9f3c]),
                        new VariableMap("IO 0x9f3d", "ushort", () => $"0x{_emulator.Memory[0x9f3d]:X2}", () => _emulator.Memory[0x9f3d]),
                        new VariableMap("IO 0x9f3e", "ushort", () => $"0x{_emulator.Memory[0x9f3e]:X2}", () => _emulator.Memory[0x9f3e]),
                        new VariableMap("IO 0x9f3f", "ushort", () => $"0x{_emulator.Memory[0x9f3f]:X2}", () => _emulator.Memory[0x9f3f]),
                    })
                    ));

        scope.AddVariable(new VariableMap("Frame", "uint", () => $"{_emulator.Vera.Frame_Count}", () => _emulator.Vera.Frame_Count));

        scope.AddVariable(new VariableMemory("VRam", () => "0x20000 bytes", "vram", () => _emulator.Vera.Vram.ToArray()));

        scope = GetNewScope("VERA Audio");

        scope.AddVariable(new VariableMap("PCM Read Position", "uint", () => $"{_emulator.VeraAudio.PcmBufferRead}", () => _emulator.VeraAudio.PcmBufferRead));
        scope.AddVariable(new VariableMap("PCM Write Position", "uint", () => $"{_emulator.VeraAudio.PcmBufferWrite}", () => _emulator.VeraAudio.PcmBufferWrite));
        scope.AddVariable(new VariableMap("PCM Sample Rate", "uint", () => $"{_emulator.VeraAudio.PcmSampleRate}", () => _emulator.VeraAudio.PcmSampleRate));
        scope.AddVariable(new VariableMap("PCM Volume", "uint", () => $"{_emulator.VeraAudio.PcmVolume}", () => _emulator.VeraAudio.PcmVolume));
        scope.AddVariable(new VariableMap("PCM Mode", "", () => _emulator.VeraAudio.PcmMode switch
        {
            0 => "8Bit Mono",
            1 => "8Bit Stereo",
            2 => "16Bit Mono",
            3 => "16Bit Stereo",
            _ => $"?? {_emulator.VeraAudio.PcmMode}"
        }));

        scope.AddVariable(
            Register(
                new VariableChildren("PSG", () => "",
                new IVariableItem[] {
                    Register(new VariableIndex("Voices", _psgManager.GetFunction))
                }
            )));

        _psgManager.Register(this);

        scope = GetNewScope("VERA FX");

        scope.AddVariable(new VariableMap("Addr Mode", "", () => _emulator.VeraFx.AddrMode switch
        {
            0 => "Normal",
            1 => "Line Draw",
            2 => "Polygon",
            3 => "Affine",
            _ => $"?? {_emulator.VeraFx.AddrMode}"
        }));
        scope.AddVariable(new VariableMap("Cache", "uint", () => $"0x{_emulator.VeraFx.Cache:X8}", () => _emulator.VeraFx.Cache));
        scope.AddVariable(new VariableMap("Cache Index", "uint", () => _emulator.VeraFx.CacheIndex, () => _emulator.VeraFx.CacheIndex));
        scope.AddVariable(new VariableMap("Cache Shift", "int", () => _emulator.VeraFx.CacheShift, () => _emulator.VeraFx.CacheShift));
        scope.AddVariable(new VariableMap("Cache Fill", "bool", () => _emulator.VeraFx.Cachefill, () => _emulator.VeraFx.Cachefill));
        scope.AddVariable(new VariableMap("Cache Write", "bool", () => _emulator.VeraFx.CacheWrite, () => _emulator.VeraFx.CacheWrite));
        scope.AddVariable(new VariableMap("4 Bit Mode", "bool", () => _emulator.VeraFx.Fx4BitMode, () => _emulator.VeraFx.Fx4BitMode));
        scope.AddVariable(new VariableMap("Transparant Writes", "bool", () => _emulator.VeraFx.TransparantWrites, () => _emulator.VeraFx.TransparantWrites));
        scope.AddVariable(new VariableMap("One-Byte Cycling", "bool", () => _emulator.VeraFx.OneByteCycling, () => _emulator.VeraFx.OneByteCycling));
        scope.AddVariable(new VariableMap("Two-Byte Cache Inc", "bool", () => _emulator.VeraFx.TwoByteCacheIncr, () => _emulator.VeraFx.TwoByteCacheIncr));
        scope.AddVariable(new VariableMap("Multiplier", "bool", () => _emulator.VeraFx.MultiplierEnable, () => _emulator.VeraFx.MultiplierEnable));
        scope.AddVariable(new VariableMap("Accumulator", "int", () => _emulator.VeraFx.Accumulator, () => _emulator.VeraFx.Accumulator));
        scope.AddVariable(new VariableMap("Accumulate Direction", "", () => _emulator.VeraFx.AccumulateDirection == 0 ? "Add" : "Subtract", () => _emulator.VeraFx.AccumulateDirection == 0 ? "Add" : "Subtract"));

        scope = GetNewScope("Kernal");

        scope.AddVariable(new VariableMap("R0", "ushort", () => $"0x{_emulator.Memory[0x02] + (_emulator.Memory[0x03] << 8):X4}", () => _emulator.Memory[0x02] + (_emulator.Memory[0x03] << 8)));
        scope.AddVariable(new VariableMap("R1", "ushort", () => $"0x{_emulator.Memory[0x04] + (_emulator.Memory[0x05] << 8):X4}", () => _emulator.Memory[0x04] + (_emulator.Memory[0x05] << 8)));
        scope.AddVariable(new VariableMap("R2", "ushort", () => $"0x{_emulator.Memory[0x06] + (_emulator.Memory[0x07] << 8):X4}", () => _emulator.Memory[0x06] + (_emulator.Memory[0x07] << 8)));
        scope.AddVariable(new VariableMap("R3", "ushort", () => $"0x{_emulator.Memory[0x08] + (_emulator.Memory[0x09] << 8):X4}", () => _emulator.Memory[0x08] + (_emulator.Memory[0x09] << 8)));

        scope.AddVariable(new VariableMap("R4", "ushort", () => $"0x{_emulator.Memory[0x0a] + (_emulator.Memory[0x0b] << 8):X4}", () => _emulator.Memory[0x0a] + (_emulator.Memory[0x0b] << 8)));
        scope.AddVariable(new VariableMap("R5", "ushort", () => $"0x{_emulator.Memory[0x0c] + (_emulator.Memory[0x0d] << 8):X4}", () => _emulator.Memory[0x0c] + (_emulator.Memory[0x0d] << 8)));
        scope.AddVariable(new VariableMap("R6", "ushort", () => $"0x{_emulator.Memory[0x0e] + (_emulator.Memory[0x0f] << 8):X4}", () => _emulator.Memory[0x0e] + (_emulator.Memory[0x0f] << 8)));
        scope.AddVariable(new VariableMap("R7", "ushort", () => $"0x{_emulator.Memory[0x10] + (_emulator.Memory[0x11] << 8):X4}", () => _emulator.Memory[0x10] + (_emulator.Memory[0x11] << 8)));

        scope.AddVariable(new VariableMap("R8", "ushort", () => $"0x{_emulator.Memory[0x12] + (_emulator.Memory[0x13] << 8):X4}", () => _emulator.Memory[0x12] + (_emulator.Memory[0x13] << 8)));
        scope.AddVariable(new VariableMap("R9", "ushort", () => $"0x{_emulator.Memory[0x14] + (_emulator.Memory[0x15] << 8):X4}", () => _emulator.Memory[0x14] + (_emulator.Memory[0x15] << 8)));
        scope.AddVariable(new VariableMap("R10", "ushort", () => $"0x{_emulator.Memory[0x16] + (_emulator.Memory[0x17] << 8):X4}", () => _emulator.Memory[0x16] + (_emulator.Memory[0x17] << 8)));
        scope.AddVariable(new VariableMap("R11", "ushort", () => $"0x{_emulator.Memory[0x18] + (_emulator.Memory[0x19] << 8):X4}", () => _emulator.Memory[0x18] + (_emulator.Memory[0x19] << 8)));

        scope.AddVariable(new VariableMap("R12", "ushort", () => $"0x{_emulator.Memory[0x1a] + (_emulator.Memory[0x1b] << 8):X4}", () => _emulator.Memory[0x1a] + (_emulator.Memory[0x1b] << 8)));
        scope.AddVariable(new VariableMap("R13", "ushort", () => $"0x{_emulator.Memory[0x1c] + (_emulator.Memory[0x1d] << 8):X4}", () => _emulator.Memory[0x1c] + (_emulator.Memory[0x1d] << 8)));
        scope.AddVariable(new VariableMap("R14", "ushort", () => $"0x{_emulator.Memory[0x1e] + (_emulator.Memory[0x1f] << 8):X4}", () => _emulator.Memory[0x1e] + (_emulator.Memory[0x1f] << 8)));
        scope.AddVariable(new VariableMap("R15", "ushort", () => $"0x{_emulator.Memory[0x20] + (_emulator.Memory[0x21] << 8):X4}", () => _emulator.Memory[0x20] + (_emulator.Memory[0x21] << 8)));

        scope = GetNewScope("Display");

        scope.AddVariable(new VariableMap("Beam X", "ushort", () => $"{_emulator.Vera.Beam_X}", () => _emulator.Vera.Beam_X));
        scope.AddVariable(new VariableMap("Beam Y", "ushort", () => $"{_emulator.Vera.Beam_Y}", () => _emulator.Vera.Beam_Y));

        scope = GetNewScope("I2C");

        scope.AddVariable(new VariableMap("Previous Data", "", () => $"{(((_emulator.I2c.Previous & 1) != 0) ? "DATA" : "____")} {(((_emulator.I2c.Previous & 2) != 0) ? "CLK" : "___")}"));
        scope.AddVariable(new VariableMap("Direction", "", () => $"{(_emulator.I2c.ReadWrite == 0 ? "To SMC" : "From SMC")}"));
        scope.AddVariable(new VariableMap("Transmitting", "uint", () => $"0x{_emulator.I2c.Transmit:X2}", () => _emulator.I2c.Transmit));
        scope.AddVariable(new VariableMap("Mode", "uint", () => $"{_emulator.I2c.Mode}", () => _emulator.I2c.Mode));
        scope.AddVariable(new VariableMap("Address", "uint", () => $"0x{_emulator.I2c.Address:X2}", () => _emulator.I2c.Address));
        scope.AddVariable(new VariableMap("Data To Transmit", "bool", () => $"{_emulator.I2c.DataToTransmit != 0}", () => _emulator.I2c.DataToTransmit != 0));

        scope = GetNewScope("SMC");

        scope.AddVariable(new VariableMap("Data", "", () => _emulator.Smc.DataCount switch
        {
            0 => "Empty",
            1 => $"0x{_emulator.Smc.Data & 0xff:X2}",
            2 => $"0x{_emulator.Smc.Data & 0xff:X2} 0x{(_emulator.Smc.Data & 0xff00) >> 8:X2}",
            _ => $"0x{_emulator.Smc.Data & 0xff:X2} 0x{(_emulator.Smc.Data & 0xff00) >> 8:X2} 0x{(_emulator.Smc.Data & 0xff0000) >> 16:X2}",
        }));

        scope.AddVariable(new VariableMap("Last Offset", "uint", () => $"0x{_emulator.Smc.Offset:X2}", () => _emulator.Smc.Offset));
        scope.AddVariable(new VariableMap("LED", "uint", () => $"0x{_emulator.Smc.Led:X2}", () => _emulator.Smc.Led));
        scope.AddVariable(new VariableMap("Keyb Read Position", "uint", () => $"0x{_emulator.Smc.SmcKeyboard_ReadPosition:X2}", () => _emulator.Smc.SmcKeyboard_ReadPosition));
        scope.AddVariable(new VariableMap("Keyb Write Position", "uint", () => $"0x{_emulator.Smc.SmcKeyboard_WritePosition:X2}", () => _emulator.Smc.SmcKeyboard_WritePosition));
        scope.AddVariable(new VariableMap("Keyb No Data", "bool", () => $"{_emulator.Smc.SmcKeyboard_ReadNoData != 0}", () => _emulator.Smc.SmcKeyboard_ReadNoData != 0));

        scope = GetNewScope("RTC");

        scope.AddVariable(Register(new VariableIndex("NVRam", GetRtcnvRam)));

        scope = GetNewScope("VIA");

        scope.AddVariable(new VariableMap("A In Value ", "", () => ViaByteDisplay(_emulator.Via.Register_A_InValue, '0', '1'), () => _emulator.Via.Register_A_InValue));
        scope.AddVariable(new VariableMap("A Direction", "", () => ViaByteDisplay(_emulator.Via.Register_A_Direction, '^', 'v')));
        scope.AddVariable(new VariableMap("A Out Value", "", () => ViaByteDisplay(_emulator.Via.Register_A_OutValue, '0', '1'), () => _emulator.Via.Register_A_OutValue));
        scope.AddVariable(new VariableMap("A Value    ", "", () => ViaByteDisplay(_emulator.Memory[0x9f01], '0', '1'), () => _emulator.Memory[0x9f01]));

        scope.AddVariable(new VariableMap("Timer1", "", () => _emulator.Via.Timer1_Counter.ToString(), () => _emulator.Via.Timer1_Counter));
        scope.AddVariable(new VariableMap("Timer1 Continuous", "", () => _emulator.Via.Timer1_Continous.ToString(), () => _emulator.Via.Timer1_Continous));
        scope.AddVariable(new VariableMap("Timer1 Running", "", () => _emulator.Via.Timer1_Running.ToString(), () => _emulator.Via.Timer1_Running));
        scope.AddVariable(new VariableMap("Timer1 Latch", "", () => _emulator.Via.Timer1_Latch.ToString(), () => _emulator.Via.Timer1_Latch));
        scope.AddVariable(new VariableMap("Timer1 Interupt", "", () => _emulator.Via.Interrupt_Timer1.ToString(), () => _emulator.Via.Interrupt_Timer1));
        scope.AddVariable(new VariableMap("Timer1 Pb7", "", () => _emulator.Via.Timer1_Pb7.ToString(), () => _emulator.Via.Timer1_Pb7));

        scope.AddVariable(new VariableMap("Timer2", "", () => _emulator.Via.Timer2_Counter.ToString(), () => _emulator.Via.Timer2_Counter));
        scope.AddVariable(new VariableMap("Timer2 Running", "", () => _emulator.Via.Timer2_Running.ToString(), () => _emulator.Via.Timer2_Running));
        scope.AddVariable(new VariableMap("Timer2 Latch", "", () => _emulator.Via.Timer2_Latch.ToString(), () => _emulator.Via.Timer2_Latch));
        scope.AddVariable(new VariableMap("Timer2 Interupt", "", () => _emulator.Via.Interrupt_Timer2.ToString(), () => _emulator.Via.Interrupt_Timer2));
        scope.AddVariable(new VariableMap("Timer2 Pulse Count", "", () => _emulator.Via.Timer2_PulseCount.ToString(), () => _emulator.Via.Timer2_PulseCount));

        scope.AddVariable(new VariableMap("Interupt Cb1", "", () => _emulator.Via.Interrupt_Cb1.ToString(), () => _emulator.Via.Interrupt_Cb1));
        scope.AddVariable(new VariableMap("Interupt Cb2", "", () => _emulator.Via.Interrupt_Cb2.ToString(), () => _emulator.Via.Interrupt_Cb2));
        scope.AddVariable(new VariableMap("Interupt Shift", "", () => _emulator.Via.Interrupt_ShiftRegister.ToString(), () => _emulator.Via.Interrupt_ShiftRegister));
        scope.AddVariable(new VariableMap("Interupt Ca1", "", () => _emulator.Via.Interrupt_Ca1.ToString(), () => _emulator.Via.Interrupt_Ca1));
        scope.AddVariable(new VariableMap("Interupt Ca2", "", () => _emulator.Via.Interrupt_Ca2.ToString(), () => _emulator.Via.Interrupt_Ca2));


        scope.AddVariable(new VariableMap("IO 0x9f00 PRB", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f00], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f00]));
        scope.AddVariable(new VariableMap("IO 0x9f01 PRA", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f01], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f01]));
        scope.AddVariable(new VariableMap("IO 0x9f02 DRB", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f02], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f02]));
        scope.AddVariable(new VariableMap("IO 0x9f03 DRA", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f03], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f03]));
        scope.AddVariable(new VariableMap("IO 0x9f04 T1L", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f04], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f04]));
        scope.AddVariable(new VariableMap("IO 0x9f05 T1H", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f05], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f05]));
        scope.AddVariable(new VariableMap("IO 0x9f06 L1L", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f06], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f06]));
        scope.AddVariable(new VariableMap("IO 0x9f07 L1H", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f07], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f07]));
        scope.AddVariable(new VariableMap("IO 0x9f08 T2L", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f08], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f08]));
        scope.AddVariable(new VariableMap("IO 0x9f09 T2H", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f09], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f09]));
        scope.AddVariable(new VariableMap("IO 0x9f0a SR ", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0a], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f0a]));
        scope.AddVariable(new VariableMap("IO 0x9f0b ACR", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0b], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f0b]));
        scope.AddVariable(new VariableMap("IO 0x9f0c PCR", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0c], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f0c]));
        scope.AddVariable(new VariableMap("IO 0x9f0d IFR", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0d], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f0d]));
        scope.AddVariable(new VariableMap("IO 0x9f0e IER", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0e], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f0e]));
        scope.AddVariable(new VariableMap("IO 0x9f0f ORA", "", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0f], 2).PadLeft(8, '0')}", () => _emulator.Memory[0x9f0f]));

        AddLocalScope("Locals");
    }

    public int GetMapSize(int value) => value switch
    {
        0 => 32,
        1 => 64,
        2 => 128,
        3 => 256,
        _ => throw new Exception("Invalid map size value")
    };

    public IEnumerable<IVariableItem> GetRamBanks()
    {
        for (var i = 0; i < 256; i++)
        {
            yield return new VariableMemory($"Ram Bank {i}", () => "", $"rambank_{i}", () => _emulator.RamBank.Slice(0x2000 * i, 0x2000).ToArray());
        }
    }

    public IEnumerable<IVariableItem> GetRomBanks()
    {
        for (var i = 0; i < 256; i++)
        {
            yield return new VariableMemory($"Rom Bank {i}", () => "", $"rombank_{i}", () => _emulator.RomBank.Slice(0x4000 * i, 0x4000).ToArray());
        }
    }

    public (string Name, ICollection<Variable> Variables) GetRtcnvRam()
    {
        var toReturn = new List<Variable>();
        var i = 0x00;
        var data = _emulator.RtcNvram;

        while (i < data.Length)
        {
            toReturn.Add(new Variable($"0x{i:X2}", $"0x{data[i]:X2}", 0));
            i++;
        }

        return ("0x40 bytes", toReturn);
    }

    private static string ViaByteDisplay(byte input, char zeroValue, char oneValue)
    {
        var sb = new StringBuilder();

        sb.Append((input & 0b10000000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b01000000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00100000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00010000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00001000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00000100) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00000010) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00000001) == 0 ? zeroValue : oneValue);
        return sb.ToString();
    }

    private static string GetColourDepth(int inp) => inp switch
    {
        0 => "1",
        1 => "2",
        2 => "4",
        3 => "8",
        _ => "??"
    };

    private static string GetT256C(bool t256c) =>
        t256c ? " T256C " : " ";
}

internal interface IScopeWrapper
{
    IScopeMap Scope { get; }
    Dictionary<string, object> ObjectTree { get; }
}

internal class ScopeWrapper : IScopeWrapper
{
    private readonly ScopeMap _scope;
    public IScopeMap Scope => _scope;
    public Dictionary<string, object> ObjectTree { get; set; } = new();

    public ScopeWrapper(ScopeMap scope)
    {
        _scope = scope;
    }

    public void AddVariable(IVariableItem variable)
    {
        _scope.AddVariable(variable);

        if (variable.GetExpressionValue != null)
            ObjectTree.Add(variable.Name, variable.GetExpressionValue);
    }

    public void Clear()
    {
        _scope.Clear();
        ObjectTree.Clear();
    }
}

/// <summary>
/// Wraps the local scope map, which provides the local variables from the source map
/// </summary>
//internal class LocalScopeWrapper : IScopeWrapper
//{
//    public IScopeMap Scope { get; set; }
//    public Dictionary<string, object> ObjectTree => Scope.Variables.ToDictionary(i => i.Name, i => (object)i.GetVariable().Value);

//    public LocalScopeWrapper(IScopeMap scope)
//    {
//        Scope = scope;
//    }
//}

public interface IVariableItem
{
    int Id { get; }
    string Name { get; }
    Variable GetVariable(); // Get the Variable from a variable request
    Func<object>? GetValue { get; } // Get value for Variables
    Action<string>? SetValue { get; } // Set value from a call to SetVariable
    void SetVariable(SetVariableArguments value); // From a variable edit
    Func<object>? GetExpressionValue { get; }  // used by the Watches
}

public abstract class VariableItem : IVariableItem
{
    public int Id { get; set; }
    public string Name { get; }
    public Func<object>? GetExpressionValue { get; protected set; }

    internal string Type { get; set; } = "";
    internal KindValue Kind { get; set; }
    internal AttributesValue Attributes { get; set; }
    internal string? MemoryReference { get; set; }

    public Func<object>? GetValue { get; protected set; }
    public Action<string>? SetValue { get; protected set; }
    public abstract void SetVariable(SetVariableArguments value);

    protected VariableItem(string name)
    {
        Name = name;
    }

    protected VariableItem(string name, Func<string> getValue)
    {
        Name = name;
        GetValue = getValue;
        GetExpressionValue = getValue;
    }

    protected VariableItem(string name, Func<string> getValue, Action<string> setValue)
    {
        Name = name;
        GetValue = getValue;
        GetExpressionValue = getValue;
        SetValue = setValue;
    }

    public virtual Variable GetVariable() => new Variable()
    {
        Name = Name,
        Type = Type,
        Value = GetValue == null ? "" : ExpressionManager.Stringify(GetValue()),
        PresentationHint = new VariablePresentationHint() { Kind = Kind, Attributes = Attributes },
        MemoryReference = MemoryReference
    };
}

public class VariableChildren : VariableItem
{
    private readonly Dictionary<string, IVariableItem> _children;

    public VariableChildren(string name, Func<string> getValue, string variableType, IVariableItem[] children) : base(name, getValue)
    {
        _children = children.ToDictionary(i => i.Name, i => i);
        GetExpressionValue = () => _children.ToDictionary(i => i.Key, i => (object?)i.Value.GetExpressionValue);
        Attributes = AttributesValue.ReadOnly;
        Type = variableType;
    }

    public VariableChildren(string name, Func<string> getValue, IVariableItem[] children) : this(name, getValue, "", children)
    {
    }

    public override void SetVariable(SetVariableArguments value)
    {
        if (!_children.ContainsKey(value.Name))
            return; // throw error?

        var child = _children[value.Name];
        if (child.SetValue != null)
            child.SetValue(value.Value);
    }

    public override Variable GetVariable() => new Variable()
    {
        Name = Name,
        Type = Type,
        Value = GetValue == null ? "" : ExpressionManager.Stringify(GetValue()),
        PresentationHint = new VariablePresentationHint() { Kind = Kind, Attributes = Attributes },
        MemoryReference = MemoryReference,
        NamedVariables = _children.Count,
        VariablesReference = Id
    };

    public IEnumerable<IVariableItem> Children => _children.Values;
}

public class VariableIndex : VariableItem
{
    private Func<(string Value, ICollection<Variable> Variables)> GetChildValues { get; }

    public VariableIndex(string name, Func<(string Value, ICollection<Variable> Variables)> getChildValeus) : base(name)
    {
        GetChildValues = getChildValeus;
        GetValue = () => GetChildValues().Value;
        // need to change the constructor here. Need a value, rather than a variable.
        GetExpressionValue = () => GetChildValues().Variables.Select(i => i.Value).ToArray();
        Attributes = AttributesValue.ReadOnly;
        Type = "";
    }

    public override void SetVariable(SetVariableArguments value)
    {
        // not sure what to do here.
    }

    public override Variable GetVariable()
    {
        (string Value, ICollection<Variable> Variables) = GetChildValues();

        return new Variable()
        {
            Name = Name,
            Type = Type,
            Value = Value,
            PresentationHint = new VariablePresentationHint() { Kind = Kind, Attributes = Attributes },
            MemoryReference = MemoryReference,
            IndexedVariables = Variables.Count,
            VariablesReference = Id
        };
    }

    public IEnumerable<Variable> GetChildren() => GetChildValues().Variables;
}

// Doesn't actually return the memory values.
public class VariableMemory : VariableItem
{
    public VariableMemory(string name, Func<string> getValue, string memoryReference, Func<byte[]> getValues) : base(name, getValue)
    {
        MemoryReference = memoryReference;
        Type = "";
        GetExpressionValue = () => new MemoryWrapper(getValues);
    }

    // shouldn't get called.
    public override void SetVariable(SetVariableArguments value)
    {
    }
}

public class VariableNotKnownException : Exception
{
    public VariableNotKnownException(string message) : base(message) { }
}

public class MemoryWrapper
{
    internal readonly Func<byte[]> _values;
    public MemoryWrapper(Func<byte[]> values)
    {
        _values = values;
    }

    public MemoryLocation this[int index]
    {
        get
        {
            return new MemoryLocation(_values, index);
        }
    }

    public class MemoryLocation
    {
        internal readonly Func<byte[]> _values;
        internal int _index;
        internal MemoryLocation(Func<byte[]> values, int index)
        {
            _values = values;
            _index = index;
        }

        public byte Byte => _values()[_index];
        public sbyte Sbyte => (sbyte)_values()[_index];
        public char Char => (char)_values()[_index];
        public short Short => BitConverter.ToInt16(_values(), _index);
        public ushort Ushort => BitConverter.ToUInt16(_values(), _index);
        public int Int => BitConverter.ToInt32(_values(), _index);
        public uint Uint => BitConverter.ToUInt32(_values(), _index);
        public long Long => BitConverter.ToInt64(_values(), _index);
        public ulong Ulong => BitConverter.ToUInt64(_values(), _index);
        public string String
        {
            get
            {
                var sb = new StringBuilder();

                var values = _values();

                for (var i = _index; i < values.Length && values[i] != 0 && i < _index + 1024; i++)
                    sb.Append((char)values[i]);

                return sb.ToString();
            }
        }
        public string FixedString(int length)
        {
            var sb = new StringBuilder();

            var values = _values();

            for (var i = _index; i < values.Length && i < length + _index; i++)
                sb.Append((char)values[i]);

            return sb.ToString();
        }

        public override string ToString() => _values()[_index].ToString();
    }
}


internal class VariableMap : IVariableItem
{
    private readonly Variable _variable;
    public string Name => _variable.Name;
    public int Id => 0;

    public Func<object> GetValue { get; }

    public Action<string>? SetValue => throw new NotImplementedException();

    public Func<object>? GetExpressionValue { get; }

    public VariableMap(string name, string type, Func<object> getFunction,
        VariablePresentationHint.KindValue kindValue = VariablePresentationHint.KindValue.Property,
        VariablePresentationHint.AttributesValue attribute = AttributesValue.None
        ) : this(name, type, getFunction, getFunction, kindValue, attribute)
    {
    }

    public VariableMap(string name, string type, Func<object> getFunction, Func<object> getExpression,
        VariablePresentationHint.KindValue kindValue = VariablePresentationHint.KindValue.Property,
        VariablePresentationHint.AttributesValue attribute = AttributesValue.None
    )
    {
        _variable = new Variable()
        {
            Name = name,
            Type = type,
            PresentationHint = new VariablePresentationHint() { Kind = kindValue, Attributes = attribute }
        };

        GetValue = getFunction;
        GetExpressionValue = getExpression;
    }

    public Variable GetVariable()
    {
        _variable.Value = ExpressionManager.Stringify(GetValue());
        return _variable;
    }

    public void SetVariable(SetVariableArguments value)
    {
        throw new NotImplementedException();
    }
}