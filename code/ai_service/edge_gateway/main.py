import argparse
import logging
import os
import signal
import threading

from .camera_sync import CameraManager, start_camera_sync
from .config import CameraCatalog, load_video_ingest_config
from .evidence_recorder import VideoEvidenceRecorder
from .live_viewer import DetectionHub, LiveFrameHub, start_live_viewer_server
from .transport.ai_bbox_sender import FanOutFrameSink, HttpAiBboxSender, WebSocketAiBboxSender
from .video_ingest import VideoIngestWorker


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Edge Gateway: read RTSP, serve live viewer and send sampled frames to the local AI Service.")
    parser.add_argument("--config", default="config.yaml", help="Path to edge/video ingest YAML config.")
    parser.add_argument(
        "--mode",
        choices=["viewer", "viewer-ai"],
        default=os.getenv("EDGE_MODE", "viewer-ai"),
        help="viewer: chi stream video. viewer-ai: stream video va gui frame sang AI Service.",
    )
    parser.add_argument(
        "--ai-url",
        default=os.getenv("AI_SERVICE_WS_URL") or os.getenv("AI_SERVICE_URL", "ws://localhost:8003/ws/detect"),
        help="AI Service endpoint: ws(s)://host:8003/ws/detect hoac http(s)://host:8003/detect.",
    )
    parser.add_argument(
        "--ai-transport",
        choices=["websocket", "http"],
        default=os.getenv("AI_SERVICE_TRANSPORT", "websocket"),
    )
    parser.add_argument("--timeout-seconds", type=float, default=float(os.getenv("AI_SERVICE_TIMEOUT_SECONDS", "10")))
    parser.add_argument("--confidence", type=float, default=float(os.getenv("YOLO_CONFIDENCE", "0.35")))
    parser.add_argument("--yolo-classes", default=os.getenv("YOLO_CLASSES", "person,car,motorcycle"))
    parser.add_argument("--tenant-id", default=os.getenv("TENANT_ID"))
    parser.add_argument(
        "--viewer-fps",
        type=float,
        default=float(os.getenv("EDGE_VIEWER_FPS", os.getenv("VIEWER_FPS", "0"))),
        help="Optional FPS override for Gateway -> Viewer stream. 0 keeps config value.",
    )
    parser.add_argument("--viewer-host", default=os.getenv("EDGE_VIEWER_HOST", "0.0.0.0"))
    parser.add_argument("--viewer-port", type=int, default=int(os.getenv("EDGE_VIEWER_PORT", "8002")))
    parser.add_argument("--ai-queue-size", type=int, default=int(os.getenv("EDGE_AI_QUEUE_SIZE", "4")))
    parser.add_argument("--ai-fps", type=float, default=float(os.getenv("EDGE_AI_FPS", os.getenv("AI_FPS", "5"))))
    parser.add_argument(
        "--camera-sync-url",
        default=os.getenv("EDGE_CAMERA_SYNC_URL", ""),
        help="Danh sach camera cua Zone Service (vd. http://zone-service:8010/api/cameras). Rong: chi dung file cau hinh.",
    )
    parser.add_argument("--camera-sync-seconds", type=float, default=float(os.getenv("EDGE_CAMERA_SYNC_SECONDS", "5")))
    return parser.parse_args()


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s - %(message)s")
    args = parse_args()
    ingest_config = load_video_ingest_config(args.config)
    catalog = CameraCatalog()
    live_hub = LiveFrameHub([])
    detection_hub = DetectionHub()
    evidence_recorder = VideoEvidenceRecorder(live_hub.frames_between)
    viewer_thread = start_live_viewer_server(
        live_hub,
        catalog,
        host=args.viewer_host,
        port=args.viewer_port,
        detection_hub=detection_hub,
        viewer_mode="sync" if args.mode == "viewer-ai" else "live",
        evidence_recorder=evidence_recorder,
    )
    if args.mode == "viewer-ai":
        sender_kwargs = dict(
            detection_hub=detection_hub,
            timeout_seconds=args.timeout_seconds,
            confidence_threshold=args.confidence,
            yolo_classes=args.yolo_classes,
            tenant_id=args.tenant_id,
            max_queue_size=args.ai_queue_size,
            target_fps=args.ai_fps,
        )
        if args.ai_transport == "websocket":
            ai_sender = WebSocketAiBboxSender(websocket_url=args.ai_url, **sender_kwargs)
        else:
            ai_sender = HttpAiBboxSender(detect_url=args.ai_url, **sender_kwargs)
        sender = FanOutFrameSink(live_hub, ai_sender)
    else:
        sender = live_hub
    stop_event = threading.Event()

    def stop_handler(signum, frame) -> None:
        logging.info("Stop signal received")
        stop_event.set()

    signal.signal(signal.SIGINT, stop_handler)
    signal.signal(signal.SIGTERM, stop_handler)

    manager = CameraManager(
        ingest_config,
        sender,
        catalog,
        on_camera_started=lambda camera: live_hub.set_camera_name(camera.camera_id, camera.name),
        fps_override=args.viewer_fps,
    )
    # Chay ngay cac camera trong file cau hinh; neu bat dong bo thi danh sach trong database
    # (Zone Service) thay the khi lay duoc, gom ca camera them tu Web Dashboard.
    manager.apply(ingest_config.cameras)
    if args.camera_sync_url:
        start_camera_sync(manager, args.camera_sync_url, os.getenv("SERVICE_API_KEY"), args.camera_sync_seconds, stop_event)

    try:
        while not stop_event.is_set():
            stop_event.wait(0.5)
    except KeyboardInterrupt:
        logging.info("Keyboard interrupt received")
    finally:
        stop_event.set()
        workers = manager.workers()
        manager.stop_all()
        sender.close()
        evidence_recorder.close()
        viewer_thread.join(timeout=1.0)
        for worker in workers:
            logging.info("Edge gateway stopped camera=%s sent_frames=%s", worker.camera.camera_id, worker.enqueued_frames)


if __name__ == "__main__":
    main()
