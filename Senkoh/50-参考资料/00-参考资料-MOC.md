---
title: "Shingen 参考资料索引"
summary: "汇总实现参考、协议布局、外部 API 快照与历史审计，并标明它们相对于当前源码的权威边界。"
aliases:
  - "参考资料 MOC"
  - "专项资料索引"
tags:
  - "scope/reference"
  - "doc/moc"
  - "area/navigation"
project: "Shingen"
doc_type: "moc"
status: "current"
authority: "curated-index"
up:
  - "[[00-导航/00-Shingen-知识库首页|Shingen 知识库首页]]"
related:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC|Fuyutsui MOC]]"
  - "[[40-跨项目/00-Shingen-跨项目契约-MOC|跨项目契约 MOC]]"
source_files: []
source_symbols: []
verified_at: "2026-08-10"
---

# Shingen 参考资料索引

> [!summary] 使用原则
> 本目录保存深度实现说明、外部 API 版本快照和历史审计。当前行为仍以源码、功能页和跨项目契约为准；标记为 `version-pinned` 或 `historical` 的资料不能直接当作当前实现规格。

## 当前实现参考

- [[50-参考资料/BLOCK_AI_Reference_zh-CN|Fuyutsui core/block.lua：AI 技术参考]]：主像素行、CountBars、光环容器和治疗吸收网格。
- [[50-参考资料/CLASSMACROS_AI_Reference_zh-CN|Fuyutsui ClassMacros：AI 宏规则参考]]：宏声明、槽位展开和覆盖绑定顺序。
- [[50-参考资料/TEXTURE_LAYOUT_zh-CN|Fuyutsui 纹理排序说明]]：`ClassBlocks` 索引分配及辅助条布局。

## 外部与历史资料

- [[50-参考资料/AuraContainer_AI_Reference_zh-CN|World of Warcraft AuraContainer：AI 技术参考]]：固定版本的外部 API 资料；先检查版本和 `verified_at`。
- [[50-参考资料/OPTIMIZATION_zh-CN|Fuyutsui 优化建议]]：历史静态审计；所有结论都应回到当前源码复核。

## 阅读路由

| 问题 | 先读 | 再核对 |
|---|---|---|
| 像素、颜色或辅助条 | [[50-参考资料/BLOCK_AI_Reference_zh-CN|block.lua 参考]] | [[40-跨项目/01-Shingen-像素生产消费契约|像素契约]] |
| 字段顺序或纹理布局 | [[50-参考资料/TEXTURE_LAYOUT_zh-CN|纹理排序说明]] | [[40-跨项目/02-Shingen-ClassBlocks到config同步契约|ClassBlocks 契约]] |
| 宏槽位或热键 | [[50-参考资料/CLASSMACROS_AI_Reference_zh-CN|ClassMacros 参考]] | [[40-跨项目/03-Shingen-ClassMacros到keymap与按键契约|宏与热键契约]] |
| AuraContainer API | [[50-参考资料/AuraContainer_AI_Reference_zh-CN|AuraContainer 参考]] | [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成|本地集成]] |
| 旧优化建议 | [[50-参考资料/OPTIMIZATION_zh-CN|历史审计]] | [[40-跨项目/04-Shingen-兼容性变更检查清单|兼容性检查清单]] |

## 关系

- 上级：[[00-导航/00-Shingen-知识库首页|Shingen 知识库首页]]
- 实现入口：[[20-Fuyutsui/00-Fuyutsui-MOC|Fuyutsui MOC]]
- 契约入口：[[40-跨项目/00-Shingen-跨项目契约-MOC|跨项目契约 MOC]]
