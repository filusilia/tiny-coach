# Agent Note: 页面出现 v7.41 更新的根因深度分析与彻底剔除 OpenDota 粗粒度版本污染

Status: implemented
Date: 2026-09-29
Class: bug-fix

## 问题背景与现场因果机制排查

1. **异常现象**：
   在客户端英雄与物品数据库页面头部，再次出现了“v7.41 更新”，丢失了现行官方细粒度平衡性字母版本（如 7.41f），出现版本倒退假象。

2. **现场真实网络探测与因果链闭环验证**：
   通过在真实环境下使用 Node 与 Go 进行底层网络与 API 探测，捕获到了完整的因果机制：
   - **事实 1：Valve 官方权威数据源确为 7.41f**：
     通过网络探测 Valve 官方接口 `https://www.dota2.com/datafeed/patchnoteslist?language=schinese`，其返回的最后 5 个补丁为：
     `7.41b` -> `7.41c` -> `7.41d` -> `7.41e` -> **`7.41f`**。官方最新版本确为 `7.41f`。
   - **事实 2：本地内网安全设备对 dota2.com 实施中间人 (MITM) 拦截**：
     在直连环境下请求 `dota2.com` 时，本地网络行为管理/防火墙（如 1.1.1.2 审计设备）对该域名进行阻断与自签名证书拦截，导致 TLS 握手直接报错：
     `tls: failed to verify certificate: x509: certificate signed by unknown authority`。
     此时，第一阶段官方接口拉取失败。
   - **事实 3：OpenDota 的降级逻辑是产生 "7.41" 的唯一罪魁祸首**：
     官方接口受阻后，Go 后端 `SystemService.FetchLatestPatchInfo()` 执行了降级逻辑，请求 OpenDota API：`https://api.opendota.com/api/constants/patch`。
     **OpenDota 官方数据事实**：其字典与常量表中**仅收录整数大版本（Major Patches：7.39, 7.40, 7.41），根本不收录任何次级字母平衡性补丁（如 7.41a~7.41f）**！其返回数组的最后一项正是 `{"name": "7.41"}`。
   - **事实 4：粗粒度大版本冲刷并污染了当前版本状态**：
     原代码执行 `latestVersion = validOd[len(validOd)-1]`，把 `latestVersion` 赋值为 `"7.41"`。由于随后拉取 7.41 官方详细说明再次因网络失败，代码进入 fallback 阶段返回了 `{"version": "7.41", ...}`，最终被前端消费渲染为 `v7.41 更新`！

## 决策与彻底根治方案

1. **彻底剔除 OpenDota 接口作为版本号来源的降级逻辑**：
   - OpenDota 常量表的粒度天然不满足 Dota 2 现行平衡性补丁要求，从它这里获取版本号注定会导致版本倒退；
   - 彻底删除针对 `https://api.opendota.com/api/constants/patch` 的版本探测代码，阻断粗粒度版本污染源头。

2. **坚持零伪造铁律与优雅置空**：
   - 当 Valve 官方接口因内网拦截或断网无法获取时，版本号严格置空 `""`，绝不退化展示虚假的 `"7.41"`；
   - 前端接收到空版本时，展示“版本待同步”或“新补丁更新”，杜绝出现伪造的 `v7.41`。

3. **优先保证有效缓存与多重容错**：
   - 若本地已成功缓存过有效的细粒度版本（如 `7.41f`），在网络受限时优先从本地缓存加载；
   - 网络请求配置更加稳健的 Transport 与代理适配，避免直接被中间人证书拦截。
