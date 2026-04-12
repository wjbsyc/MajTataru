"""
MajTataru Mortal MJAI Server

使用本地 model.py + engine.py 加载 Mortal 模型，
通过 libriichi.mjai.Bot 处理 MJAI 协议事件。

依赖:
  pip install flask torch numpy
  需要预编译的 libriichi (含 mjai.Bot)
  将 mortal.pth 放到本目录下

启动:
  python server.py [--port 7331] [--model mortal.pth]
"""

import json
import argparse
import pathlib
import time

import torch
import numpy as np
from flask import Flask, request, jsonify

from model import Brain, DQN
from engine import MortalEngine

app = Flask(__name__)

bot_instance = None
last_events_len = 0
last_resp = None


def load_engine(model_path: str, device: torch.device) -> MortalEngine:
    state = torch.load(model_path, map_location=device, weights_only=False)
    cfg = state["config"]
    version = cfg["control"]["version"]
    conv_channels = cfg["resnet"]["conv_channels"]
    num_blocks = cfg["resnet"]["num_blocks"]

    brain = Brain(version=version, conv_channels=conv_channels, num_blocks=num_blocks).eval()
    dqn = DQN(version=version).eval()
    brain.load_state_dict(state["mortal"])
    dqn.load_state_dict(state["current_dqn"])

    engine = MortalEngine(
        brain,
        dqn,
        version=version,
        is_oracle=False,
        device=device,
        enable_amp=device.type == "cuda",
        enable_rule_based_agari_guard=True,
        name="mortal",
    )
    return engine


class MjaiBot:
    def __init__(self, engine: MortalEngine):
        self.engine = engine
        self.bot = None
        self.player_id = None
        self.extra_events = 0

    def ensure_bot(self, events):
        if self.bot is not None:
            return
        for e in events:
            if e.get("type") == "start_game":
                self.player_id = e.get("id", 0)
                from libriichi.mjai import Bot
                self.bot = Bot(self.engine, self.player_id)
                print(f"[INFO] Bot 已创建, player_id={self.player_id}")
                return

    def react(self, new_events):
        if self.bot is None or not new_events:
            return None

        self.extra_events = 0
        start = time.time()
        return_action = None

        for e in new_events:
            e_str = json.dumps(e, separators=(",", ":"), ensure_ascii=False)
            print(f"  → {e_str}")
            try:
                return_action = self.bot.react(
                    json.dumps(e, separators=(",", ":")))
                if return_action is not None:
                    print(f"  ← {return_action}")
            except Exception as ex:
                print(f"[ERROR] react({e.get('type', '?')}): {ex}")
                return_action = None

        elapsed = time.time() - start
        if elapsed > 0.5:
            print(f"[INFO] 推理耗时: {elapsed:.2f}s")

        if return_action is None:
            return None

        raw = json.loads(return_action)
        raw.pop("meta", None)

        if raw.get("type") == "reach":
            print(f"  [reach 两步] 喂回 reach 事件获取打牌...")
            reach_event = {"type": "reach", "actor": raw.get("actor", self.player_id)}
            reach_str = json.dumps(reach_event, separators=(",", ":"))
            print(f"  → {reach_str}")
            try:
                dahai_action = self.bot.react(reach_str)
                if dahai_action is not None:
                    print(f"  ← {dahai_action}")
                    dahai_raw = json.loads(dahai_action)
                    dahai_raw.pop("meta", None)
                    raw["pai"] = dahai_raw.get("pai")
                    raw["tsumogiri"] = dahai_raw.get("tsumogiri", False)
                self.extra_events = 1
            except Exception as ex:
                print(f"[ERROR] reach follow-up: {ex}")

        return json.dumps(raw, separators=(",", ":"))

    def reset(self):
        self.bot = None
        self.player_id = None


@app.route("/reset", methods=["POST"])
def handle_reset():
    global bot_instance, last_events_len, last_resp
    bot_instance.reset()
    last_events_len = 0
    last_resp = None
    print("[INFO] 收到重置请求，Bot 已重置")
    return json.dumps({"status": "ok"}), 200, {"Content-Type": "application/json"}


@app.route("/", methods=["POST"])
def handle_request():
    global bot_instance, last_events_len, last_resp

    try:
        events = request.get_json(force=True)
        if not isinstance(events, list):
            return jsonify({"type": "none", "error": "expected JSON array"}), 400
    except Exception as e:
        return jsonify({"type": "none", "error": str(e)}), 400

    if len(events) < last_events_len:
        bot_instance.reset()
        last_events_len = 0
        last_resp = None
        print(f"[INFO] 事件列表缩短 -> {len(events)}，重置 Bot")

    new_events = events[last_events_len:]
    last_events_len = len(events)

    if not new_events:
        result = last_resp
    else:
        bot_instance.ensure_bot(events)
        types = [e.get("type", "?") for e in new_events]
        print(f"[DEBUG] 处理 {len(new_events)} 个新事件: {types}")
        result = bot_instance.react(new_events)
        if bot_instance.extra_events > 0:
            last_events_len += bot_instance.extra_events
        if result is not None:
            last_resp = result

    if result is None:
        result = json.dumps({"type": "none"})

    print(f"[DEBUG] 返回: {result}")
    return result, 200, {"Content-Type": "application/json"}


def main():
    global bot_instance

    parser = argparse.ArgumentParser(description="MajTataru Mortal MJAI Server")
    parser.add_argument("--port", type=int, default=7331,
                        help="监听端口 (默认: 7331)")
    parser.add_argument("--host", default="127.0.0.1",
                        help="监听地址 (默认: 127.0.0.1)")
    parser.add_argument("--model", default="mortal.pth",
                        help="模型文件路径 (默认: mortal.pth)")
    args = parser.parse_args()

    model_path = pathlib.Path(__file__).parent / args.model
    if not model_path.exists():
        print(f"[ERROR] 模型文件不存在: {model_path}")
        return

    if torch.cuda.is_available():
        device = torch.device("cuda")
        print(f"[INFO] 使用 GPU: {torch.cuda.get_device_name(0)}")
    else:
        device = torch.device("cpu")
        print("[INFO] 使用 CPU")

    print(f"[INFO] 加载模型: {model_path}")
    engine = load_engine(str(model_path), device)
    print("[INFO] MortalEngine 加载成功")

    bot_instance = MjaiBot(engine)

    print(f"[INFO] MajTataru Mortal Server 启动于 http://{args.host}:{args.port}")
    print(f"[INFO] 在 MajTataru 插件中填入: http://{args.host}:{args.port}")
    app.run(host=args.host, port=args.port, debug=False)


if __name__ == "__main__":
    main()
