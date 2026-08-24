-- Fuyutsui -> EXBoss 数据桥接。
-- EXBoss 不存在时，本文件不注册任何 EX 事件，也不创建技能像素。

local Fuyutsui = _G.Fuyutsui
if not Fuyutsui then return end

local START_EVENT = "EXBOSS_BOSS_OBSERVED_CAST_START"
local STOP_EVENT = "EXBOSS_BOSS_OBSERVED_CAST_STOP"
local OWNER = "Fuyutsui.ExBoss"
local MAX_BLOCK_INDEX = 510

-- [spellID] = 像素的逻辑名称。
-- 名称相同的 spellID 共用一个顶部像素。
local spells = {
    [381516] = "打断施法",
}

local ExBoss = Fuyutsui.ExBoss or {}
Fuyutsui.ExBoss = ExBoss
ExBoss.spells = spells
ExBoss.spellPixels = ExBoss.spellPixels or {}
ExBoss.pixelGroups = ExBoss.pixelGroups or {}
ExBoss.bound = false
ExBoss.nativeBound = false
ExBoss.layoutBlocks = nil

local function IsAddonLoaded(name)
    if C_AddOns and type(C_AddOns.IsAddOnLoaded) == "function" then
        return C_AddOns.IsAddOnLoaded(name) == true
    end
    return type(IsAddOnLoaded) == "function" and IsAddOnLoaded(name) == true
end

local function NormalizeSpellID(value)
    if type(issecretvalue) == "function" and issecretvalue(value) then
        return nil
    end
    local id = tonumber(value)
    return id and id > 0 and id or nil
end

local function GetMaxBlockIndex(value, currentMax)
    if type(value) ~= "table" then
        return currentMax
    end

    for key, child in pairs(value) do
        if key == "index" and type(child) == "number" then
            currentMax = math.max(currentMax, math.floor(child))
        elseif type(child) == "table" then
            currentMax = GetMaxBlockIndex(child, currentMax)
        end
    end

    return currentMax
end

local function GetSortedSpellIDs()
    local ids = {}
    for spellID in pairs(spells) do
        ids[#ids + 1] = spellID
    end
    table.sort(ids)
    return ids
end

local function ClearSpellState(entry)
    if type(entry) ~= "table" then return end
    entry.active = false
    entry.runtimeID = nil
    entry.unit = nil
    entry.castKind = nil
    entry.castBarID = nil
    entry.totalDuration = 0
    entry.startedAt = 0
end

local function EnsureSpellPixels()
    local blocks = Fuyutsui.blocks
    if type(blocks) ~= "table" or type(blocks.state) ~= "table" or next(blocks.state) == nil then
        return false
    end
    if ExBoss.layoutBlocks == blocks then
        return true
    end

    local nextIndex = GetMaxBlockIndex(blocks, 0) + 1
    local newPixels = {}
    local newGroups = {}

    for _, spellID in ipairs(GetSortedSpellIDs()) do
        local name = spells[spellID]
        local group = newGroups[name]
        if not group then
            if nextIndex > MAX_BLOCK_INDEX then
                print("|cffff0000[Fuyutsui] EXBoss 技能像素空间不足，无法创建 " .. tostring(name) .. "|r")
                break
            end

            group = {
                name = name,
                index = nextIndex,
                spellIDs = {},
            }
            newGroups[name] = group
            nextIndex = nextIndex + 1
        end

        local entry = ExBoss.spellPixels[spellID] or {}
        entry.spellID = spellID
        entry.name = name
        entry.group = group
        entry.index = group.index
        group.spellIDs[#group.spellIDs + 1] = spellID
        ClearSpellState(entry)
        newPixels[spellID] = entry
    end

    ExBoss.spellPixels = newPixels
    ExBoss.pixelGroups = newGroups
    ExBoss.layoutBlocks = blocks
    return true
end

local function GetCurrentDuration(entry)
    if type(entry) ~= "table" or not entry.active or not entry.unit then
        return nil
    end

    if entry.castKind == "channel" then
        return UnitChannelDuration(entry.unit)
    end
    return UnitCastingDuration(entry.unit)
end

local function GetActiveGroupEntry(group)
    if type(group) ~= "table" then return nil end

    local activeEntry
    for _, spellID in ipairs(group.spellIDs or {}) do
        local entry = ExBoss.spellPixels[spellID]
        if entry and entry.active then
            if not activeEntry or (tonumber(entry.startedAt) or 0) > (tonumber(activeEntry.startedAt) or 0) then
                activeEntry = entry
            end
        end
    end
    return activeEntry
end

local function UpdatePixelGroup(group)
    if type(group) ~= "table" or not group.index then return end

    local value = 0
    local duration = GetCurrentDuration(GetActiveGroupEntry(group))
    if duration then
        local color = duration:EvaluateRemainingDuration(Fuyutsui.castCurve)
        local _, _, blue = color:GetRGB()
        value = blue or 0
    end

    Fuyutsui:CreateTexture(group.index, value)
end

local function ReadNativeSpellID(unit, castKind, eventSpellID)
    local spellID = NormalizeSpellID(eventSpellID)
    if spellID then return spellID end

    if castKind == "channel" then
        local ok, _, _, _, _, _, _, _, apiSpellID = pcall(UnitChannelInfo, unit)
        return ok and NormalizeSpellID(apiSpellID) or nil
    end

    local ok, _, _, _, _, _, _, _, _, apiSpellID = pcall(UnitCastingInfo, unit)
    return ok and NormalizeSpellID(apiSpellID) or nil
end

local function OnBossCastStart(_, payload)
    if type(payload) ~= "table" then return end

    local spellID = NormalizeSpellID(payload.spellID)
    local name = spellID and spells[spellID]
    if not name then return end
    local entry = ExBoss.spellPixels[spellID]
    local runtimeID = tonumber(payload.runtimeID)
    local castBarID = tonumber(payload.castBarID)
    local now = GetTime()
    local duplicateStart = entry and entry.active
        and ((runtimeID ~= nil and entry.runtimeID == runtimeID)
            or (runtimeID == nil and castBarID ~= nil and entry.castBarID == castBarID)
            or (entry.unit == payload.unit and math.abs(now - (tonumber(entry.startedAt) or 0)) <= 0.5))

    if not duplicateStart then
        print("开始施放指定技能")
    end

    if not EnsureSpellPixels() then return end
    entry = ExBoss.spellPixels[spellID]
    if not entry then return end

    entry.active = true
    entry.runtimeID = runtimeID
    entry.unit = type(payload.unit) == "string" and payload.unit or nil
    entry.castKind = tostring(payload.castKind or "cast")
    entry.castBarID = castBarID
    entry.totalDuration = tonumber(payload.totalDuration) or 0
    entry.startedAt = tonumber(payload.startedAt) or now
    UpdatePixelGroup(entry.group)
end

local function OnBossCastStop(_, payload)
    if type(payload) ~= "table" then return end

    local spellID = NormalizeSpellID(payload.spellID)
    local runtimeID = tonumber(payload.runtimeID)

    if spellID and ExBoss.spellPixels[spellID] then
        local entry = ExBoss.spellPixels[spellID]
        if runtimeID == nil or entry.runtimeID == nil or entry.runtimeID == runtimeID then
            ClearSpellState(entry)
            UpdatePixelGroup(entry.group)
        end
        return
    end

    for _, entry in pairs(ExBoss.spellPixels) do
        if runtimeID ~= nil and entry.runtimeID == runtimeID then
            ClearSpellState(entry)
            UpdatePixelGroup(entry.group)
        end
    end
end

local function OnNativeBossCastStart(_, event, ...)
    local unit = select(1, ...)
    if type(unit) ~= "string" or not unit:match("^boss[1-5]$") then return end

    local eventSpellID = select(3, ...)
    local castBarID = select(select("#", ...), ...)
    local castKind = event == "UNIT_SPELLCAST_CHANNEL_START" and "channel" or "cast"
    local spellID = ReadNativeSpellID(unit, castKind, eventSpellID)
    if not spellID or not spells[spellID] then return end

    OnBossCastStart(nil, {
        spellID = spellID,
        unit = unit,
        castKind = castKind,
        castBarID = castBarID,
        startedAt = GetTime(),
    })
end

local function OnNativeBossCastStop(_, event, ...)
    local unit = select(1, ...)
    if type(unit) ~= "string" or not unit:match("^boss[1-5]$") then return end

    local eventSpellID = select(3, ...)
    local castBarID = select(select("#", ...), ...)
    local castKind = event == "UNIT_SPELLCAST_CHANNEL_STOP" and "channel" or "cast"
    local spellID = NormalizeSpellID(eventSpellID)

    for _, entry in pairs(ExBoss.spellPixels) do
        local sameUnit = entry.active and entry.unit == unit
        local sameKind = entry.castKind == castKind
        local sameCastBar = castBarID == nil or entry.castBarID == nil or entry.castBarID == castBarID
        local sameSpell = spellID == nil or entry.spellID == spellID
        if sameUnit and sameKind and sameCastBar and sameSpell then
            local group = entry.group
            ClearSpellState(entry)
            UpdatePixelGroup(group)
        end
    end
end

local nativeFrame = CreateFrame("Frame")
local function BindNativeBossEvents()
    if ExBoss.nativeBound then return end

    nativeFrame:RegisterEvent("UNIT_SPELLCAST_START")
    nativeFrame:RegisterEvent("UNIT_SPELLCAST_CHANNEL_START")
    nativeFrame:RegisterEvent("UNIT_SPELLCAST_STOP")
    nativeFrame:RegisterEvent("UNIT_SPELLCAST_CHANNEL_STOP")
    nativeFrame:RegisterEvent("UNIT_SPELLCAST_INTERRUPTED")
    nativeFrame:RegisterEvent("UNIT_SPELLCAST_FAILED")
    nativeFrame:RegisterEvent("UNIT_SPELLCAST_FAILED_QUIET")
    nativeFrame:SetScript("OnEvent", function(frame, event, ...)
        if event == "UNIT_SPELLCAST_START" or event == "UNIT_SPELLCAST_CHANNEL_START" then
            OnNativeBossCastStart(frame, event, ...)
        else
            OnNativeBossCastStop(frame, event, ...)
        end
    end)
    ExBoss.nativeBound = true
end

local function BindExBossEvents()
    if ExBoss.bound then return true end
    if not IsAddonLoaded("EXBoss") then return false end

    local ExwindTools = _G.ExwindTools
    if not ExwindTools or type(ExwindTools.RegisterEvent) ~= "function" then
        return false
    end

    ExwindTools:RegisterEvent(START_EVENT, OWNER .. ".Start", OnBossCastStart)
    ExwindTools:RegisterEvent(STOP_EVENT, OWNER .. ".Stop", OnBossCastStop)
    BindNativeBossEvents()
    ExBoss.bound = true
    return true
end

local updateFrame = CreateFrame("Frame")
updateFrame:SetScript("OnUpdate", function()
    if not ExBoss.bound then return end
    if not EnsureSpellPixels() then return end

    for _, group in pairs(ExBoss.pixelGroups) do
        UpdatePixelGroup(group)
    end
end)

local driver = CreateFrame("Frame")
driver:RegisterEvent("ADDON_LOADED")
driver:RegisterEvent("PLAYER_LOGIN")
driver:RegisterEvent("PLAYER_ENTERING_WORLD")
driver:RegisterEvent("PLAYER_TALENT_UPDATE")
driver:SetScript("OnEvent", function(_, event, addonName)
    if event == "ADDON_LOADED" then
        if addonName == "ExwindTools" or addonName == "EXBoss" then
            BindExBossEvents()
        end
        return
    end

    if event == "PLAYER_LOGIN" or event == "PLAYER_ENTERING_WORLD" or event == "PLAYER_TALENT_UPDATE" then
        BindExBossEvents()
        EnsureSpellPixels()
    end
end)

-- EXBoss 可能比 Fuyutsui 更早加载。
BindExBossEvents()
