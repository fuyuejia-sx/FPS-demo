# FPS Demo（地铁站第一人称射击游戏）

一个基于 **Unity 6 (6000.6.9f1)** 与 **URP (Universal Render Pipeline)** 开发的第一人称射击（FPS）演示项目。游戏以地铁站站台为战斗场景，包含第一人称移动与射击、盾牌防护、普通敌人与带骨骼动画的僵尸敌人、击杀金币奖励、实时 3D 陈列柜商城、武器背包切换、拾取式弹药补给以及完整的暂停/结算/退出流程。

---

## 一、项目简介

- **引擎版本**：Unity `6000.6.9f1`
- **渲染管线**：Universal Render Pipeline (`com.unity.render-pipelines.universal` 17.6.0)
- **输入系统**：Unity New Input System (`com.unity.inputsystem` 1.20.1)
- **UI 框架**：UGUI + TextMesh Pro (`com.unity.ugui` 2.6.0)，`Canvas Scaler` 采用 `Scale With Screen Size (1920 × 1080)`
- **寻路方案**：内置 `NavMeshBuilder` 运行时动态烘焙（无需额外安装 AI Navigation 包）
- **主场景**：`Assets/Scenes/GetStarted_Scene.unity`

---

## 二、游戏玩法与操作说明

### 1. 基础玩法循环
1. **探索与战斗**：玩家在地铁站台内与两类敌人（无骨骼机器人敌人、带完整骨骼动画的僵尸敌人）交战，利用枪械消灭敌人，或切出盾牌格挡近战伤害。
2. **获取金币**：开局拥有 **100 金币**；每击杀 1 个无骨骼敌人奖励 **+10 金币**，每击杀 1 个带骨骼敌人奖励 **+20 金币**。右上角实时显示当前金币总数，左下角弹出本次击杀获得的金币数。
3. **3D 陈列柜商城购物**：按 **`B`** 键打开商城，在实时 3D 陈列柜中浏览并拖拽旋转查看武器模型，使用金币购买新武器 **PULSE BLASTER（脉冲步枪，80 金币）**。
4. **替换与背包管理**：购买新武器后会弹出确认框选择**立即替换当前主武器**或**保持原枪**；未被使用的武器自动收纳到背包中。按 **`I`** 键可随时打开背包，在已拥有的武器之间自由切换（保留各武器剩余弹药）。
5. **拾取弹药补给**：站台地面分布有发光的绿色弹药补给箱，走近即可为当前主武器补充备弹（默认每箱 `+30`，15 秒自动刷新）。弹药严格受武器最大上限约束，满弹时不会超过上限也不会白白消耗补给箱。
6. **胜负与结算**：消灭场景内全部敌人触发 **VICTORY**，生命值归零触发 **GAME OVER**；结算界面及 **`Esc`** 暂停菜单均支持鼠标点击重新开始或退出游戏。

### 2. 操作按键

| 按键 | 功能 |
| :--- | :--- |
| `W / A / S / D` | 前后左右移动 |
| `Shift`（按住） | 向前冲刺 |
| `Space` | 跳跃 |
| 鼠标移动 | 第一人称视角转动 |
| 鼠标左键 | 开火（支持全自动连发） |
| `R` | 换弹 / 结算界面快速重新开始 |
| `1` / `2` | 在当前主武器与盾牌之间切换 |
| `B` | 打开 / 关闭 3D 陈列柜商城 |
| `I` | 打开 / 关闭武器背包 |
| `Esc` | 关闭当前界面（商城/背包）或打开 / 关闭暂停菜单 |

---

## 三、核心功能模块

| 模块 | 核心脚本（相对路径） | 功能说明 |
| :--- | :--- | :--- |
| **输入封装** | `Assets/FPS/Scripts/Input/PlayerInputReader.cs` | 统一封装 New Input System，对外提供移动、视角、开火、换弹、切装备等输入状态 |
| **玩家控制** | `Assets/FPS/Scripts/Player/PlayerMovement.cs`<br>`Assets/FPS/Scripts/Player/PlayerLook.cs` | 基于 `CharacterController` 的移动/冲刺/跳跃/重力，以及第一人称俯仰视角与鼠标锁定管理 |
| **装备与盾牌** | `Assets/FPS/Scripts/Player/PlayerEquipment.cs`<br>`Assets/FPS/Scripts/Player/PlayerShield.cs` | 快捷栏装备切换（主武器 + 盾牌）与槽位替换；持盾时优先由盾值吸收伤害，盾值耗尽自动收起 |
| **武器与弹药** | `Assets/FPS/Scripts/Weapons/Weapon.cs`<br>`Assets/FPS/Scripts/Weapons/WeaponAmmo.cs`<br>`Assets/FPS/Scripts/Weapons/WeaponEffects.cs`<br>`Assets/FPS/Scripts/Weapons/PlayerWeaponController.cs` | Hitscan 射线射击、弹匣/备弹与最大弹药上限控制、自动/手动换弹、枪口闪光/后坐力/命中标记，以及多武器动态切换 |
| **生命与伤害** | `Assets/FPS/Scripts/Combat/Health.cs`<br>`Assets/FPS/Scripts/Combat/DamageInfo.cs`<br>`Assets/FPS/Scripts/Combat/IDamageable.cs`<br>`Assets/FPS/Scripts/Combat/IDamageAbsorber.cs` | 统一生命值管理、伤害吸收接口与死亡延迟销毁（配合死亡动画播放完毕后再消失） |
| **敌人 AI 与寻路** | `Assets/FPS/Scripts/Enemies/EnemyAI.cs`<br>`Assets/FPS/Scripts/Enemies/RuntimeNavMeshBaker.cs` | 待机/巡逻/追击/近战攻击/死亡状态机、受击仇恨、视线检测；支持带骨骼敌人播放走路/攻击/死亡动画；运行时自动构建 NavMesh |
| **金币奖励** | `Assets/FPS/Scripts/Core/CoinManager.cs` | 管理初始金币（100）、监听击杀事件区分无骨骼（+10）与带骨骼敌人（+20）发奖，提供加减金币接口 |
| **3D 陈列柜商城** | `Assets/FPS/Scripts/Shop/ShopManager.cs`<br>`Assets/FPS/Scripts/Shop/ShopItemData.cs`<br>`Assets/FPS/Scripts/Shop/ShopDisplayCabinet.cs`<br>`Assets/FPS/Scripts/Shop/ShopUI.cs` | 离屏独立搭建带灯光/底座/玻璃隔断的实时 3D 陈列柜，渲染至 `RenderTexture` 嵌入商城界面；支持拖拽 360° 旋转查看、金币购买及购买后“是否替换”弹窗 |
| **武器背包** | `Assets/FPS/Scripts/Inventory/WeaponInventory.cs`<br>`Assets/FPS/Scripts/Inventory/BackpackUI.cs` | 自动收纳所有已拥有但未使用的主武器，支持在背包界面查看属性、剩余弹药并一键替换当前主武器 |
| **弹药补给拾取** | `Assets/FPS/Scripts/Pickups/AmmoPickup.cs` | 触发器拾取补给箱，带旋转悬浮动效与定时刷新机制；严格遵守最大弹药上限，满弹不消耗并提示 |
| **流程与 HUD** | `Assets/FPS/Scripts/Core/GameManager.cs`<br>`Assets/FPS/Scripts/UI/HUDController.cs` | 胜负判定、ESC 暂停菜单、`Application.Quit()` 退出游戏、光标自动锁定/解锁，以及血条/盾条/弹药/敌人数/金币/拾取提示等完整 HUD |
| **编辑器辅助工具** | `Assets/FPS/Editor/FPSSceneSetup.cs`<br>`Assets/FPS/Editor/FPSModelSwap.cs`<br>`Assets/FPS/Editor/FPSRiggedEnemy.cs` | 提供 Unity 顶部 `FPS/` 一键菜单，自动化完成场景配置、模型替换、骨骼敌人导入与动画同步、商城配置和弹药箱摆放（位于 `Editor/` 目录，打包时自动剔除） |

---

## 四、模型与美术资源相对路径

所有模型、动画、贴图及生成材质均集中存放在 `Assets/FPS/` 下（以下均为相对项目根目录的路径）：

### 1. 武器与装备模型

| 资源用途 | 模型文件路径 | 贴图 / 材质路径 |
| :--- | :--- | :--- |
| **初始主武器（步枪）** | `Assets/FPS/Models/gun/base.fbx` | 贴图目录：`Assets/FPS/Models/gun/texture_*.png`<br>生成材质：`Assets/FPS/Materials/ImportedGun.mat` |
| **防护盾牌** | `Assets/FPS/Models/shield/base.fbx` | 贴图目录：`Assets/FPS/Models/shield/texture_*.png`<br>生成材质：`Assets/FPS/Materials/ImportedShield.mat` |
| **商城脉冲步枪（Blaster）** | `Assets/FPS/Models/BlasterKit/blaster-d.fbx` | 贴图：`Assets/FPS/Models/BlasterKit/Textures/colormap.png`<br>材质：`Assets/FPS/Materials/Blaster.mat` |

### 2. 敌人模型与骨骼动画

| 资源用途 | 模型 / 动画文件路径 | 贴图 / 材质 / 控制器路径 |
| :--- | :--- | :--- |
| **无骨骼敌人（机器人）** | `Assets/FPS/Models/target/base.fbx` | 贴图目录：`Assets/FPS/Models/target/texture_*.png`（含自发光）<br>生成材质：`Assets/FPS/Materials/ImportedEnemy.mat` |
| **带骨骼敌人（僵尸：走路）** | `Assets/FPS/Animations/Zombie Walk.fbx` | 漫反射贴图：`Assets/FPS/Animations/texture_diffuse.png`<br>材质：`Assets/FPS/Materials/RiggedEnemy.mat`<br>状态机：`Assets/FPS/Animations/RiggedEnemy.controller` |
| **带骨骼敌人（僵尸：攻击）** | `Assets/FPS/Animations/Zombie Attack.fbx` | 同上，由 `RiggedEnemy.controller` 的 `Attack` 状态引用 |
| **带骨骼敌人（僵尸：死亡）** | `Assets/FPS/Animations/Zombie Death.fbx` | 同上，由 `RiggedEnemy.controller` 的 `Death` 状态引用 |
| **备用角色模型（SpaceKit）** | `Assets/FPS/Models/SpaceKit/alien.fbx` | 许可说明：`Assets/FPS/Models/SpaceKit/License.txt` |

### 3. 场景环境与道具材质

| 资源用途 | 模型文件路径 | 贴图 / 材质路径 |
| :--- | :--- | :--- |
| **地铁站室内场景** | `Assets/FPS/Models/evn/pack/combined_world/fbx/scene_pbr.fbx` | PBR 贴图目录：`Assets/FPS/Models/evn/pack/combined_world/fbx/texture_pbr/`<br>生成材质：`Assets/FPS/Materials/Station_object_0000.mat` ~ `Station_object_0004.mat`<br>灯罩/玻璃材质：`Assets/FPS/Materials/StationLight.mat`、`Assets/FPS/Materials/StationGlass.mat`<br>Blender 工程备份：`Assets/FPS/Models/evn/pack/blender/scene.blend` |
| **弹药补给箱材质** | 由 `FPSSceneSetup` 组合生成 | `Assets/FPS/Materials/AmmoCrate.mat`<br>`Assets/FPS/Materials/AmmoCrateAccent.mat` |
| **合成金属度/光滑度贴图** | 自动生成 | `Assets/FPS/Materials/Generated/*_MetallicSmoothness.png` |

---

## 五、项目目录结构

```text
.
├── Assets/
│   ├── Scenes/
│   │   └── GetStarted_Scene.unity       # 主游戏场景
│   └── FPS/
│       ├── Animations/                  # 僵尸骨骼模型、走路/攻击/死亡动画、漫反射贴图与 Animator Controller
│       ├── Editor/                      # Unity 编辑器工具脚本（仅编辑器生效，不进入打包）
│       ├── Materials/                   # 武器、盾牌、敌人、地铁站与弹药箱材质及合成贴图
│       ├── Models/                      # 3D 模型与贴图（BlasterKit / evn / gun / shield / SpaceKit / target）
│       └── Scripts/
│           ├── Combat/                  # 生命值与伤害接口
│           ├── Core/                    # 游戏流程管理（暂停/结算/退出）与金币系统
│           ├── Enemies/                 # 敌人 AI 状态机与运行时 NavMesh 构建
│           ├── Input/                   # New Input System 输入读取封装
│           ├── Inventory/               # 武器背包逻辑与界面
│           ├── Pickups/                 # 拾取式弹药补给箱
│           ├── Player/                  # 玩家移动、视角、装备切换与盾牌
│           ├── Shop/                    # 商城核心、商品数据、实时 3D 陈列柜与商城界面
│           ├── UI/                      # HUD 界面、暂停菜单与结算界面
│           └── Weapons/                 # 射线武器、弹药上限控制与枪口/后坐力特效
├── Packages/
│   └── manifest.json                    # Unity 依赖包清单
└── ProjectSettings/                     # 项目与构建设置
```

---

## 六、编辑器工具与打包指南

### 1. 编辑器一键菜单（Unity 顶部菜单栏 `FPS/`）
- **`FPS > 一键配置场景`**：补全场景中的所有核心组件、HUD、导入模型、商城、带骨骼敌人及弹药补给箱。
- **`FPS > 替换为下载的模型`**：重新把场景地形、枪、盾和普通敌人替换为 `Assets/FPS/Models/` 下的导入模型。
- **`FPS > 添加带骨骼的敌人`**：在站台空地新增一名带骨骼动画的僵尸敌人。
- **`FPS > 更新带骨骼的敌人动画与材质`**：同步所有带骨骼敌人的 Animator 状态机、贴图材质与攻击/死亡时长。
- **`FPS > 配置商城与陈列柜`**：单独配置商城、脉冲步枪 `Blaster`、武器背包与 `EventSystem`。
- **`FPS > 放置弹药补给箱`**：在站台地面自动摆放 3 个发光弹药补给箱。

### 2. 打包构建说明
1. 打开 `Assets/Scenes/GetStarted_Scene.unity`，确认已保存最新场景改动（`Ctrl + S`）。
2. 在 Unity 菜单打开 **File > Build Profiles**（确认 `Assets/Scenes/GetStarted_Scene.unity` 已勾选在场景列表中）。
3. 点击 **Build** 生成可执行文件：
   - 所有编辑器脚本均已隔离在 `Assets/FPS/Editor/` 或 `#if UNITY_EDITOR` 中，可直接打包无报错。
   - 打包全屏运行后，按 `Esc` 可唤出暂停菜单或通过结算面板的 `QUIT GAME` 按钮正常退出游戏，打开任意界面均会自动解锁并显示鼠标光标。
