--[[
摘要：
    Fuyutsui 前端面板原型：15 秒爆发计时条（悬停 Tooltip 三行按键说明；点击按按键分派：
    左键重置/右键取消/中键长计时）+ 三个状态切换按钮（AOE/循环/药水）。
描述：
    面板无标题栏、初始居中，采用 PhantomProject 手法：WindowBorder 1px 边框 + WindowBg
    内缩填充；上层为爆发计时条（轨道全透明、填充 SliderLeft 蓝，纯进度条无文字，
    点击按按键分派：左键重置 15 秒、右键取消、中键 1 小时长计时，随时间收缩或归零隐藏，
    每帧刷新），悬停显示三行按键说明 Tooltip（左键/右键/中键，照 BindRowHover 手法）；
    下层为三个等宽切换按钮（BUTTON_BORDER=1
    边框 + 内缩填充，悬停/按下变色，文字用 GameFontHighlightSmall 并随状态翻转改色）。
    全部尺寸为固定 UI 像素，取自文件头全大写常量（PANEL_WIDTH/ROW_HEIGHT/BAR_HEIGHT/
    SPACING/PANEL_BORDER/BUTTON_BORDER/CLICK_THRESHOLD/FONT_SIZE），不做缩放换算；面板可拖动，
    位置不保存，每次加载回到屏幕中间。计时条按下时记录光标位置，抬起时位移小于
    CLICK_THRESHOLD 像素判定为点击并按按键分派（左键重置/右键取消/中键长计时），位移大则视为拖动。
主要变量信息：
    PANEL_WIDTH/ROW_HEIGHT/BAR_HEIGHT/SPACING/PANEL_BORDER/BUTTON_BORDER/
        CLICK_THRESHOLD/FONT_SIZE：文件头集中定义的固定 UI 像素尺寸常量
        （BAR_HEIGHT 为计时条高度，严格垂直居中于 ROW_HEIGHT 行内），全部布局尺寸直接引用
    burstTime：爆发计时截止时间戳（GetTime 纪元），初始 0；点击计时条按按键分派：左键 +15 秒、
        右键 -1 秒取消、中键 +3600 秒长计时
    buttonDefs：三个按钮的默认态/点击态文字与颜色定义表
    panel：面板根框体（全局命名 FuyutsuiBurstPanel，子元素全部匿名）
修改记录：
    2026-08-17：计时条层新增悬停 Tooltip（左键/右键/中键三行按键说明，照 BindRowHover 手法）
    2026-08-17：新增 Fuyutsui 前端面板原型（爆发计时条 + 三按钮切换）
    2026-08-17：修改 按冻结后变更（提交 9d82699）取消 UI 缩放换算，尺寸改为文件头
        全大写常量；字体改为从 GameFontHighlightSmall 取字体文件/阴影并固定字号
    2026-08-17：微调轮——轨道全透明（删 SliderRight 配色）、条高 BAR_HEIGHT=10 并严格
        垂直居中、文件级变量/常量补行尾中文注释并同步修正过期注释
    2026-08-17：修复计时条归零残段——SetWidth(0) 清除 desired width 导致 1px 残留，
        归零改为隐藏填充（OnUpdate 中 remaining<=0 或轨道宽无效时 fill:Hide）
    2026-08-17：计时条点击按按键分派——左键 +15 重置 / 右键 -1 取消（remaining 恒负走归零
        Hide）/ 中键 +3600 长计时（clamp 封顶满宽），其余按键维持 +15；同步修正相关注释
--]]

local addon, ns = ... -- 保持 Fuyutsui 文件惯例，本文件不引用

-- 固定 UI 像素尺寸常量（文件头集中定义，所有尺寸直接引用，不做缩放换算）
local PANEL_WIDTH = 120   -- 面板总宽（固定 UI 像素）
local ROW_HEIGHT = 16     -- 单行高度：计时条行与按钮行各占一行
local BAR_HEIGHT = 10     -- 计时条高度：原 ROW_HEIGHT-2*SPACING=12 基础上用户要求减 2（建议保持在 ROW_HEIGHT 内）
local SPACING = 2         -- 计时条与层边、两行之间、按钮之间的间距
local PANEL_BORDER = 1    -- 面板外边框宽度
local BUTTON_BORDER = 1   -- 按钮外边框宽度
local CLICK_THRESHOLD = 5 -- 点击判定阈值：抬起时位移小于该像素数视为点击（按按键分派）
local FONT_SIZE = 12      -- 按钮文字字号

-- 配色表（PhantomProject 全量配色去除 SliderRight（轨道透明）+ 状态色），全部 CreateColor 创建
local Black = CreateColor(0 / 255, 0 / 255, 0 / 255, 1)              -- 纯黑（PhantomProject 配色，本原型未引用）
local WindowBg = CreateColor(30 / 255, 30 / 255, 30 / 255, 1)        -- 面板内缩填充底色
local WindowText = CreateColor(0 / 255, 0 / 255, 0 / 255, 1)         -- 窗口文字黑（PhantomProject 配色，本原型未引用）
local WindowBorder = CreateColor(83 / 255, 88 / 255, 91 / 255, 1)    -- 面板外边框灰
local Base = CreateColor(255 / 255, 255 / 255, 255 / 255, 1)         -- 基础白（PhantomProject 配色，本原型未引用）
local ButtonBorder = CreateColor(52 / 255, 52 / 255, 52 / 255, 1)    -- 按钮边框灰
local ButtonHighlight = CreateColor(86 / 255, 86 / 255, 86 / 255, 1) -- 按钮悬停时边框亮灰
local ButtonMouseUp = CreateColor(43 / 255, 43 / 255, 43 / 255, 1)   -- 按钮填充常态灰
local ButtonMouseDown = CreateColor(37 / 255, 37 / 255, 37 / 255, 1) -- 按钮按下时填充暗灰
local SliderLeft = CreateColor(255 / 255, 79 / 255, 79 / 255, 1)     -- 计时条填充蓝
local RowHover = CreateColor(50 / 255, 50 / 255, 50 / 255, 1)        -- 行悬停灰（PhantomProject 配色，本原型未引用）
local Text = CreateColor(230 / 255, 230 / 255, 230 / 255, 1)         -- 常规文字浅灰（PhantomProject 配色，本原型未引用）
local DropdownBg = CreateColor(34 / 255, 34 / 255, 34 / 255, 1)      -- 下拉框底色（PhantomProject 配色，本原型未引用）
local StateGreen = CreateColor(0.30, 0.75, 0.40, 1)                  -- 状态绿：按钮"开启/默认"态文字色
local StateYellow = CreateColor(0.85, 0.75, 0.30, 1)                 -- 状态黄：按钮"AOE/爆发药开启"态文字色
local StateBlue = CreateColor(0.41, 0.80, 0.94, 1)                   -- 状态蓝：按钮"单体/官方"态文字色

-- 面板根框体：全局命名 FuyutsuiBurstPanel，居中、可拖动、位置不保存
local panel = CreateFrame("Frame", "FuyutsuiBurstPanel", UIParent)      -- 面板根框体（全局命名，子元素全部匿名）
panel:SetSize(PANEL_WIDTH, 2 * PANEL_BORDER + SPACING + 2 * ROW_HEIGHT) -- 总高 = 上边框 1 + 计时条行 16 + 间距 2 + 按钮行 16 + 下边框 1 = 36
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

-- 面板外观：BACKGROUND 铺满设边框色，ARTWORK 内缩 PANEL_BORDER 设填充色
local panelBg = panel:CreateTexture(nil, "BACKGROUND") -- 面板边框层：铺满面板设外边框灰
panelBg:SetAllPoints()
panelBg:SetColorTexture(WindowBorder:GetRGB())
local panelArt = panel:CreateTexture(nil, "ARTWORK") -- 面板填充层：内缩 PANEL_BORDER 设底色
panelArt:SetPoint("TOPLEFT", panel, "TOPLEFT", PANEL_BORDER, -PANEL_BORDER)
panelArt:SetPoint("BOTTOMRIGHT", panel, "BOTTOMRIGHT", -PANEL_BORDER, PANEL_BORDER)
panelArt:SetColorTexture(WindowBg:GetRGB())
-- 面板根框体结束

-- 爆发计时条层：高 ROW_HEIGHT 内缩边框，可点击按按键分派（左键重置/右键取消/中键长计时），也参与面板拖动
local burstTime = 0                               -- 爆发计时截止时间戳（GetTime 纪元），初始 0，点击计时条按按键分派：左键 +15 / 右键 -1 / 中键 +3600
local pressX, pressY = 0, 0                       -- 计时条按下时记录的光标位置，用于抬起时判定点击还是拖动

local barLayer = CreateFrame("Frame", nil, panel) -- 计时条层：高 ROW_HEIGHT，内缩 PANEL_BORDER，负责点击按按键分派与拖动
barLayer:SetPoint("TOPLEFT", panel, "TOPLEFT", PANEL_BORDER, -PANEL_BORDER)
barLayer:SetPoint("TOPRIGHT", panel, "TOPRIGHT", -PANEL_BORDER, -PANEL_BORDER)
barLayer:SetHeight(ROW_HEIGHT)
barLayer:EnableMouse(true)

-- 悬停 Tooltip：照 PhantomProject BindRowHover 手法——标题行 SetText 绿（Fuyutsui）、
-- 三行按键说明 AddLine 灰，锚定层右侧 SPACING 偏移，层级 TOOLTIP；OnMouseDown 隐藏、点击分派后由 OnMouseUp 恢复
local function ShowBurstTooltip()
    GameTooltip:SetOwner(barLayer, "ANCHOR_RIGHT", SPACING, 0)
    GameTooltip:SetFrameStrata("TOOLTIP")
    GameTooltip:SetFrameLevel(1000)
    GameTooltip:SetText("Fuyutsui", 0, 1, 0.6, 1, true)
    GameTooltip:AddLine("左键：爆发15秒", 0.8, 0.8, 0.8, true)
    GameTooltip:AddLine("右键：取消爆发", 0.8, 0.8, 0.8, true)
    GameTooltip:AddLine("中键：永久爆发", 0.8, 0.8, 0.8, true)
    GameTooltip:Show()
end
barLayer:SetScript("OnEnter", ShowBurstTooltip) -- 悬停进入显示提示
barLayer:SetScript("OnLeave", function()        -- 光标移出层后隐藏提示
    GameTooltip:Hide()
end)

-- 计时条：轨道全透明、填充蓝，条高 BAR_HEIGHT 在层内严格垂直居中（上 3 下 3），条宽 = 层宽 - 2*SPACING
local track = barLayer:CreateTexture(nil, "BACKGROUND") -- 计时条轨道（全透明，仅提供宽度/高度布局基准）
track:SetPoint("TOPLEFT", barLayer, "TOPLEFT", SPACING, -(ROW_HEIGHT - BAR_HEIGHT) / 2)
track:SetPoint("TOPRIGHT", barLayer, "TOPRIGHT", -SPACING, -(ROW_HEIGHT - BAR_HEIGHT) / 2)
track:SetHeight(BAR_HEIGHT)
track:SetColorTexture(0, 0, 0, 0)

local fill = barLayer:CreateTexture(nil, "ARTWORK") -- 计时条蓝色填充：锚在 track 左右底边，高度随 track（BAR_HEIGHT）
fill:SetPoint("TOPLEFT", track, "TOPLEFT", 0, 0)
fill:SetPoint("BOTTOMLEFT", track, "BOTTOMLEFT", 0, 0)
fill:SetColorTexture(SliderLeft:GetRGB())

-- 每帧刷新填充：显示值 = max(0, min(15, burstTime - GetTime()))，条宽按剩余比例收缩；
-- remaining 归 0（含初始 burstTime=0）或轨道宽无效时隐藏填充——SetWidth(0) 会清除 desired
-- width 导致 1px 残段，故归零改为 Hide 而不是设宽 0
barLayer:SetScript("OnUpdate", function(self)
    local remaining = burstTime - GetTime()
    if remaining > 15 then
        remaining = 15
    elseif remaining < 0 then
        remaining = 0
    end
    local trackWidth = self:GetWidth() - 2 * SPACING
    if trackWidth > 0 and remaining > 0 then
        fill:Show()
        fill:SetWidth(trackWidth * remaining / 15)
    else
        fill:Hide()
    end
end)

-- 按下记录光标位置并启动面板拖动；抬起时位移小于 CLICK_THRESHOLD 像素判定为点击，按按键分派（见下方 OnMouseUp）
barLayer:SetScript("OnMouseDown", function()
    GameTooltip:Hide() -- 按下瞬间隐藏 Tooltip，拖动过程不显示
    pressX, pressY = GetCursorPosition()
    panel:StartMoving()
end)
-- 抬起时位移小于 CLICK_THRESHOLD 像素判定为点击，按按键分派：左键重置为 15 秒、
-- 右键取消（置为过去时刻）、中键置为 1 小时长计时，其余按键维持重置
barLayer:SetScript("OnMouseUp", function(self, button)
    local x, y = GetCursorPosition()
    if math.abs(x - pressX) < CLICK_THRESHOLD and math.abs(y - pressY) < CLICK_THRESHOLD then
        if button == "LeftButton" then
            burstTime = GetTime() + 15
        elseif button == "RightButton" then
            burstTime = GetTime() - 1
        elseif button == "MiddleButton" then
            burstTime = GetTime() + 3600
        else
            burstTime = GetTime() + 15 -- Button4/Button5 等未请求变更的按键维持现状
        end
        ShowBurstTooltip()             -- 判定为点击并分派后立即恢复 Tooltip（光标仍在层内）；拖动不恢复
    end
    panel:StopMovingOrSizing()
    panel:SetUserPlaced(false) -- 清除用户放置标记，避免位置被客户端保存
end)
-- 计时条层结束

-- 三个状态切换按钮：等宽铺满按钮行，按钮间距 SPACING
local buttonRow = CreateFrame("Frame", nil, panel) -- 按钮行层：位于计时条层下方 SPACING 处，高 ROW_HEIGHT
buttonRow:SetPoint("TOPLEFT", barLayer, "BOTTOMLEFT", 0, -SPACING)
buttonRow:SetPoint("TOPRIGHT", barLayer, "BOTTOMRIGHT", 0, -SPACING)
buttonRow:SetHeight(ROW_HEIGHT)

local contentWidth = PANEL_WIDTH - 2 * PANEL_BORDER  -- 面板内缩边框后的内容区宽度
local buttonWidth = (contentWidth - 2 * SPACING) / 3 -- 单个按钮宽：内容区宽减去两端间距后三等分

-- 字体：从 GameFontHighlightSmall 取字体文件/样式标志与阴影，只取一次供所有按钮使用
local fontFile, _, fontFlags = GameFontHighlightSmall:GetFont()                    -- 字体文件路径与样式标志（居中位丢弃）
local shadowR, shadowG, shadowB, shadowA = GameFontHighlightSmall:GetShadowColor() -- 字体阴影 RGBA
local shadowOffX, shadowOffY = GameFontHighlightSmall:GetShadowOffset()            -- 字体阴影偏移

-- 按钮定义：默认态/点击态文字（字间半角空格）与颜色，点击来回翻转
local buttonDefs = { -- 三按钮的默认态/点击态文字与颜色定义表，循环创建按钮时逐项取用
    { offText = "自 动", offColor = StateGreen, onText = "单 体", onColor = StateYellow },
    { offText = "手 动", offColor = StateGreen, onText = "官 方", onColor = StateBlue },
    { offText = "不喝药", offColor = StateYellow, onText = "爆发药", onColor = StateGreen },
}

local prevButton -- 上一枚创建的按钮：用于把后续按钮依次锚在其右侧
for _, def in ipairs(buttonDefs) do
    local button = CreateFrame("Button", nil, buttonRow)

    -- PhantomProject 按钮手法：BUTTON_BORDER=1 边框 + 内缩填充
    local bg = button:CreateTexture(nil, "BACKGROUND")
    bg:SetAllPoints()
    bg:SetColorTexture(ButtonBorder:GetRGB())

    local art = button:CreateTexture(nil, "ARTWORK")
    art:SetPoint("TOPLEFT", button, "TOPLEFT", BUTTON_BORDER, -BUTTON_BORDER)
    art:SetPoint("BOTTOMRIGHT", button, "BOTTOMRIGHT", -BUTTON_BORDER, BUTTON_BORDER)
    art:SetColorTexture(ButtonMouseUp:GetRGB())

    -- 文字：沿用 GameFontHighlightSmall 字体文件/样式标志与阴影，固定 FONT_SIZE 字号，
    -- 颜色随状态（默认白色必须显式改色）
    local label = button:CreateFontString(nil, "OVERLAY")
    label:SetFont(fontFile or "Fonts\\FRIZQT__.TTF", FONT_SIZE, fontFlags)
    label:SetShadowColor(shadowR, shadowG, shadowB, shadowA)
    label:SetShadowOffset(shadowOffX, shadowOffY)
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

    -- 布局：等宽铺满，按钮间距 SPACING
    button:SetSize(buttonWidth, ROW_HEIGHT)
    if prevButton then
        button:SetPoint("TOPLEFT", prevButton, "TOPRIGHT", SPACING, 0)
    else
        button:SetPoint("TOPLEFT", buttonRow, "TOPLEFT", 0, 0)
    end
    prevButton = button
end
-- 按钮行结束
