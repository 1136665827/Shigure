--[[
摘要：
    Fuyutsui 前端面板原型：15 秒爆发计时条（点击重置）+ 三个状态切换按钮（AOE/循环/药水）。
描述：
    面板无标题栏、初始居中，采用 PhantomProject 手法：WindowBorder 1px 边框 + WindowBg
    内缩填充；上层为爆发计时条（轨道 SliderRight 灰、填充 SliderLeft 蓝，纯进度条无文字，
    点击重置为 15 秒并随时间收缩，每帧刷新），下层为三个等宽切换按钮（2px 边框 +
    内缩填充，悬停/按下变色，文字用 GameFontHighlightSmall 并随状态翻转改色）。
    所有尺寸经 S() 缩放换算（768 高度基准，等价 GetUIScaleFactor）；面板可拖动，
    位置不保存，每次加载回到屏幕中间。计时条按下时记录光标位置，抬起时位移小于
    5 缩放像素判定为点击重置，位移大则视为拖动。
主要变量信息：
    S：缩放函数，把 768 高度基准像素换算为当前 UI 像素
    burstTime：爆发计时截止时间戳（GetTime 纪元），初始 0；点击计时条置为当前时间 + 15 秒
    buttonDefs：三个按钮的默认态/点击态文字与颜色定义表
    panel：面板根框体（全局命名 FuyutsuiBurstPanel，子元素全部匿名）
修改记录：
    2026-08-17：新增 Fuyutsui 前端面板原型（爆发计时条 + 三按钮切换）
--]]

local addon, ns = ... -- 保持 Fuyutsui 文件惯例，本文件不引用

-- 缩放函数：把 768 高度基准像素换算为当前 UI 像素（等价 GetUIScaleFactor）
local function S(pixelValue)
    return pixelValue * 768 / select(2, GetPhysicalScreenSize()) / UIParent:GetScale()
end

-- 配色表（PhantomProject 全量 + 状态色），全部 CreateColor 创建
local Black = CreateColor(0 / 255, 0 / 255, 0 / 255, 1)
local WindowBg = CreateColor(30 / 255, 30 / 255, 30 / 255, 1)
local WindowText = CreateColor(0 / 255, 0 / 255, 0 / 255, 1)
local WindowBorder = CreateColor(83 / 255, 88 / 255, 91 / 255, 1)
local Base = CreateColor(255 / 255, 255 / 255, 255 / 255, 1)
local ButtonBorder = CreateColor(52 / 255, 52 / 255, 52 / 255, 1)
local ButtonHighlight = CreateColor(86 / 255, 86 / 255, 86 / 255, 1)
local ButtonMouseUp = CreateColor(43 / 255, 43 / 255, 43 / 255, 1)
local ButtonMouseDown = CreateColor(37 / 255, 37 / 255, 37 / 255, 1)
local SliderLeft = CreateColor(73 / 255, 179 / 255, 234 / 255, 1)
local SliderRight = CreateColor(159 / 255, 159 / 255, 159 / 255, 1)
local RowHover = CreateColor(50 / 255, 50 / 255, 50 / 255, 1)
local Text = CreateColor(230 / 255, 230 / 255, 230 / 255, 1)
local DropdownBg = CreateColor(34 / 255, 34 / 255, 34 / 255, 1)
local StateGreen = CreateColor(0.30, 0.75, 0.40, 1)
local StateYellow = CreateColor(0.85, 0.75, 0.30, 1)
local StateBlue = CreateColor(0.41, 0.80, 0.94, 1)

-- 面板根框体：全局命名 FuyutsuiBurstPanel，居中、可拖动、位置不保存
local panel = CreateFrame("Frame", "FuyutsuiBurstPanel", UIParent)
panel:SetSize(S(180), S(82)) -- 内容区 36 + 8 + 36，上下各加 1px 边框
panel:SetPoint("CENTER", UIParent, "CENTER")
panel:SetMovable(true)
panel:RegisterForDrag("LeftButton")
panel:SetScript("OnDragStart", function(self)
    self:StartMoving()
end)
panel:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    self:SetUserPlaced(false) -- 清除用户放置标记，避免位置被客户端保存
end)

-- 面板外观：BACKGROUND 铺满设边框色，ARTWORK 内缩 1px 设填充色
local panelBg = panel:CreateTexture(nil, "BACKGROUND")
panelBg:SetAllPoints()
panelBg:SetColorTexture(WindowBorder:GetRGB())
local panelArt = panel:CreateTexture(nil, "ARTWORK")
panelArt:SetPoint("TOPLEFT", panel, "TOPLEFT", S(1), -S(1))
panelArt:SetPoint("BOTTOMRIGHT", panel, "BOTTOMRIGHT", -S(1), S(1))
panelArt:SetColorTexture(WindowBg:GetRGB())
-- 面板根框体结束

-- 爆发计时条层：高 36 内缩边框，可点击重置，也参与面板拖动
local burstTime = 0
local pressX, pressY = 0, 0

local barLayer = CreateFrame("Frame", nil, panel)
barLayer:SetPoint("TOPLEFT", panel, "TOPLEFT", S(1), -S(1))
barLayer:SetPoint("TOPRIGHT", panel, "TOPRIGHT", -S(1), -S(1))
barLayer:SetHeight(S(36))
barLayer:EnableMouse(true)

-- 计时条：轨道灰、填充蓝，条四周距层边 8（条高 36-16=20）
local track = barLayer:CreateTexture(nil, "BACKGROUND")
track:SetPoint("TOPLEFT", barLayer, "TOPLEFT", S(8), -S(8))
track:SetPoint("BOTTOMRIGHT", barLayer, "BOTTOMRIGHT", -S(8), S(8))
track:SetColorTexture(SliderRight:GetRGB())

local fill = barLayer:CreateTexture(nil, "ARTWORK")
fill:SetPoint("TOPLEFT", track, "TOPLEFT", 0, 0)
fill:SetPoint("BOTTOMLEFT", track, "BOTTOMLEFT", 0, 0)
fill:SetColorTexture(SliderLeft:GetRGB())

-- 每帧刷新填充：显示值 = max(0, min(15, burstTime - GetTime()))
barLayer:SetScript("OnUpdate", function(self)
    local remaining = burstTime - GetTime()
    if remaining > 15 then
        remaining = 15
    elseif remaining < 0 then
        remaining = 0
    end
    local trackWidth = self:GetWidth() - 2 * S(8)
    if trackWidth > 0 then
        fill:SetWidth(trackWidth * remaining / 15)
    end
end)

-- 按下记录光标位置并启动面板拖动；抬起时位移小于 5 缩放像素判定为点击重置
barLayer:SetScript("OnMouseDown", function()
    pressX, pressY = GetCursorPosition()
    panel:StartMoving()
end)
barLayer:SetScript("OnMouseUp", function()
    local x, y = GetCursorPosition()
    if math.abs(x - pressX) < S(5) and math.abs(y - pressY) < S(5) then
        burstTime = GetTime() + 15
    end
    panel:StopMovingOrSizing()
    panel:SetUserPlaced(false) -- 清除用户放置标记，避免位置被客户端保存
end)
-- 计时条层结束

-- 三个状态切换按钮：等宽铺满按钮行，按钮间距 8
local buttonRow = CreateFrame("Frame", nil, panel)
buttonRow:SetPoint("TOPLEFT", barLayer, "BOTTOMLEFT", 0, -S(8))
buttonRow:SetPoint("TOPRIGHT", barLayer, "BOTTOMRIGHT", 0, -S(8))
buttonRow:SetHeight(S(36))

local contentWidth = S(180) - 2 * S(1)
local buttonWidth = (contentWidth - 2 * S(8)) / 3

-- 按钮定义：默认态/点击态文字（字间半角空格）与颜色，点击来回翻转
local buttonDefs = {
    { offText = "自 动", offColor = StateGreen, onText = "单 体", onColor = StateYellow },
    { offText = "手 动", offColor = StateGreen, onText = "官 方", onColor = StateBlue },
    { offText = "不喝药", offColor = StateYellow, onText = "爆发药", onColor = StateGreen },
}

local prevButton
for _, def in ipairs(buttonDefs) do
    local button = CreateFrame("Button", nil, buttonRow)

    -- PhantomProject 按钮手法：2px 边框 + 内缩填充
    local bg = button:CreateTexture(nil, "BACKGROUND")
    bg:SetAllPoints()
    bg:SetColorTexture(ButtonBorder:GetRGB())

    local art = button:CreateTexture(nil, "ARTWORK")
    art:SetPoint("TOPLEFT", button, "TOPLEFT", S(2), -S(2))
    art:SetPoint("BOTTOMRIGHT", button, "BOTTOMRIGHT", -S(2), S(2))
    art:SetColorTexture(ButtonMouseUp:GetRGB())

    -- 文字：预设字体对象，颜色随状态（GameFontHighlightSmall 默认白色，必须显式改色）
    local label = button:CreateFontString(nil, "OVERLAY")
    label:SetFontObject(GameFontHighlightSmall)
    label:SetJustifyH("CENTER")
    label:SetJustifyV("MIDDLE")
    label:SetPoint("CENTER")

    -- 状态翻转：默认 off，点击切换
    local isOn = false
    local function ApplyState()
        if isOn then
            label:SetText(def.onText)
            label:SetTextColor(def.onColor:GetRGB())
        else
            label:SetText(def.offText)
            label:SetTextColor(def.offColor:GetRGB())
        end
    end
    ApplyState()

    button:SetScript("OnClick", function()
        isOn = not isOn
        ApplyState()
    end)

    -- 悬停边框变亮、按下填充变暗（PhantomProject 反馈）
    button:SetScript("OnEnter", function()
        bg:SetColorTexture(ButtonHighlight:GetRGB())
    end)
    button:SetScript("OnLeave", function()
        bg:SetColorTexture(ButtonBorder:GetRGB())
    end)
    button:SetScript("OnMouseDown", function()
        art:SetColorTexture(ButtonMouseDown:GetRGB())
    end)
    button:SetScript("OnMouseUp", function()
        art:SetColorTexture(ButtonMouseUp:GetRGB())
    end)

    -- 布局：等宽铺满，按钮间距 8
    button:SetSize(buttonWidth, S(36))
    if prevButton then
        button:SetPoint("TOPLEFT", prevButton, "TOPRIGHT", S(8), 0)
    else
        button:SetPoint("TOPLEFT", buttonRow, "TOPLEFT", 0, 0)
    end
    prevButton = button
end
-- 按钮行结束
