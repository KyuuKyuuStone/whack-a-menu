# 右键菜单注册表速查

本文件说明 Windows 11 右键菜单项都藏在哪、本工具怎么读、怎么隐藏。手动排查时也能用。

## 1. 两个菜单

Windows 11 有两套右键菜单：

- **现代菜单（一级菜单）**：圆角、图标稀疏。第三方要进来，通常实现 `IExplorerCommand`，
  用 `ExplorerCommandHandler` 指向一个 CLSID（打包应用 / 稀疏包应用常走 `PackagedCom`）。
- **传统菜单（"显示更多选项" / Shift+F10）**：完整的 `IContextMenu` COM 扩展，
  注册在 `shellex\ContextMenuHandlers`，以及静态 `shell` verb。

Windows 11 22H2 之后，静态 `shell` verb 若带 `ExplorerCommandHandler`，会同时出现在现代菜单里；
不带的话，只出现在「显示更多选项」。

## 2. 扫描的作用域（Target）

| Target | 触发场景 |
| --- | --- |
| `*` | 选中任意文件 |
| `AllFilesystemObjects` | 任意文件 / 文件夹 |
| `Directory` | 选中文件夹 |
| `Directory\Background` | 文件夹空白处、桌面空白处 |
| `Folder` | 文件夹（含特殊文件夹） |
| `Drive` | 磁盘驱动器 |
| `lnkfile` | 快捷方式 (.lnk) |
| `SystemFileAssociations\*` | 按文件类型（本工具不递归扫描，避免噪音） |

## 3. 扫描的根键

```
HKCU\Software\Classes\<Target>\shell\<Verb>
HKCU\Software\Classes\<Target>\shellex\ContextMenuHandlers\<Handler>
HKLM\SOFTWARE\Classes\<Target>\shell\<Verb>
HKLM\SOFTWARE\Classes\<Target>\shellex\ContextMenuHandlers\<Handler>
HKCU\Software\Classes\PackagedCom\Package\<Pkg>\Class\<CLSID>
HKLM\SOFTWARE\Classes\PackagedCom\Package\<Pkg>\Class\<CLSID>
```

`HKEY_CLASSES_ROOT` 是 HKCU\Software\Classes 与 HKLM\SOFTWARE\Classes 的**合并视图**，
HKCU 优先。工具分开枚举两个 hive，才能知道「这项归谁管、要不要管理员权限」。

`ContextMenuHandlers\<Handler>` 的**默认值**就是 CLSID，再去
`HKLM\SOFTWARE\Classes\CLSID\<CLSID>` 读名称、`InprocServer32`、`DefaultIcon`。

## 4. 显示名与图标从哪来

优先级：默认值 → `MUIVerb` → `LocalizedString` → 键名。
形如 `@C:\Windows\System32\shell32.dll,-21787` 的是 **间接字符串**，要用
`SHLoadIndirectString` 展开成当前系统语言的文本。

图标优先级：

1. verb 键的 `Icon` 值（`"path.dll,3"`，负号表示资源 ID）
2. CLSID 的 `DefaultIcon` 默认值
3. `InprocServer32` / `LocalServer32` 的 DLL/EXE
4. `shell\<Verb>\command` 默认值里的 EXE
5. 回退：用名称首字画一张色块图标

用 `ExtractIconEx` 取指定序号图标，`DestroyIcon` 释放句柄；
厂商名用 `FileVersionInfo.CompanyName` 读 EXE 的版本资源。

## 5. 隐藏方式（全部可逆）

| 类型 | 操作 | 为什么有效 |
| --- | --- | --- |
| 静态 verb | `SetValue("ProgrammaticAccessOnly", "")` | 微软官方语义：菜单里不再出现，但程序仍可调用该 verb |
| 静态 verb（现代菜单） | 备份 `ExplorerCommandHandler` 到 `CMC_ExplorerCommandHandler` 后删除该值 | 现代菜单靠这个 CLSID 找到 IExplorerCommand，移除即不显示 |
| COM handler | 备份默认值到 `CMC_OriginalCLSID`，默认值置空 | Explorer 枚举 handler 时拿不到 CLSID，跳过加载 |
| PackagedCom | CLSID 写入 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` | 系统级 shell 扩展黑名单，Explorer 加载前检查 |

恢复时按 `hidden.xml` 的记录逐条反操作。记录字段包含原始 hive、完整键路径、原默认值、
是否原本就存在 `ProgrammaticAccessOnly`、黑名单写入位置，避免「恢复时误删别人设的值」。

## 6. 生效时机

Explorer 会缓存 shell 扩展。工具调用 `SHChangeNotify(SHCNE_ASSOCCHANGED, …)` 做轻量刷新；
完整生效通常需要**重启 explorer.exe**（工具提供按钮）或注销。
重启 explorer 只会让任务栏 / 桌面闪一下，不会关掉已打开的程序窗口。

## 7. 手动排查

```powershell
# 看某个文件类型的所有静态 verb
Get-ChildItem "Registry::HKEY_CLASSES_ROOT\*\shell" | Select-Object PSChildName

# 看所有 COM 右键扩展
Get-ChildItem "Registry::HKEY_CLASSES_ROOT\*\shellex\ContextMenuHandlers" |
  ForEach-Object { "{0} -> {1}" -f $_.PSChildName, $_.GetValue("") }

# 看系统黑名单
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked"
```

如果某个软件的菜单项没被工具扫到，多半是写在了 `SystemFileAssociations\<扩展名>\shell`、
`*\shellex\PropertySheetHandlers`（那是属性页，不是右键菜单）或用了私有 shell 扩展。
把键路径发到 Issue 即可补规则。

## 8. 卸载本工具

1. 打开工具 → 「已隐藏」→ 全选 → 「恢复选中项」
2. 删除 `%LOCALAPPDATA%\ContextMenuCleaner\`（仅隐藏记录）
3. 删除 exe

没有服务、没有计划任务、没有开机启动项、没有写系统目录。
