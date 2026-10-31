import os
import tempfile
import threading
import unittest

import cv2
import numpy as np

from edge_gateway.camera_sync import RUNNING_STATUSES, CameraManager
from edge_gateway.config import CameraCatalog, CameraIngestConfig, VideoIngestConfig, camera_from_catalog
from edge_gateway.video_source import FileVideoSource, RtspVideoSource, create_video_source, resolve_video_path


INGEST = VideoIngestConfig(
    reconnect_interval_seconds=0.01,
    read_retry_count=1,
    log_every_n_frames=100,
    jpeg_quality=70,
    cameras=[],
    default_target_fps=15,
    default_frame_width=640,
    default_frame_height=360,
)


def camera(camera_id, source_url="rtsp://mediamtx:8554/x", source_type="RTSP", name=None):
    return CameraIngestConfig(
        camera_id=camera_id,
        location_id="loc-1",
        name=name or camera_id,
        source_type=source_type,
        source_url=source_url,
        target_fps=15,
        frame_width=640,
        frame_height=360,
        encoding="JPEG",
    )


class FakeWorker:
    instances = []

    def __init__(self, camera, ingest_config, sink, stop_event):
        self.camera = camera
        self.stop_event = stop_event
        self.stopped = False
        self.enqueued_frames = 0
        FakeWorker.instances.append(self)

    def run(self):
        self.stop_event.wait(5)

    def stop(self):
        self.stopped = True
        self.stop_event.set()


class CameraManagerTests(unittest.TestCase):
    def setUp(self):
        FakeWorker.instances = []
        self.catalog = CameraCatalog()
        self.started = []
        self.manager = CameraManager(
            INGEST, sink=None, catalog=self.catalog,
            on_camera_started=lambda c: self.started.append(c.camera_id),
            worker_factory=FakeWorker,
        )

    def tearDown(self):
        self.manager.stop_all()

    def test_starts_new_cameras_and_registers_them_in_catalog(self):
        self.manager.apply([camera("a"), camera("b", "rtsp://mediamtx:8554/b")])
        self.assertEqual(["a", "b"], self.manager.camera_ids())
        self.assertIn("a", self.catalog)
        self.assertEqual(["a", "b"], self.started)

    def test_stops_cameras_removed_from_list(self):
        self.manager.apply([camera("a"), camera("b", "rtsp://mediamtx:8554/b")])
        self.manager.apply([camera("a")])
        self.assertEqual(["a"], self.manager.camera_ids())
        self.assertNotIn("b", self.catalog)
        self.assertTrue(FakeWorker.instances[1].stopped)

    def test_restarts_camera_when_source_changes_but_not_when_unchanged(self):
        self.manager.apply([camera("a")])
        self.manager.apply([camera("a")])
        self.assertEqual(1, len(FakeWorker.instances))
        self.manager.apply([camera("a", "vtest.avi", "FILE")])
        self.assertEqual(2, len(FakeWorker.instances))
        self.assertTrue(FakeWorker.instances[0].stopped)
        self.assertEqual("FILE", self.catalog.get("a").source_type)

    def test_name_or_ai_detail_change_updates_worker_without_restart(self):
        self.manager.apply([camera("a")])
        renamed = camera("a", name="Cam A moi")
        renamed = renamed.__class__(**{**renamed.__dict__, "ai_image_size": 1280})
        self.manager.apply([renamed])
        self.assertEqual(1, len(FakeWorker.instances))
        self.assertFalse(FakeWorker.instances[0].stopped)
        self.assertEqual(1280, FakeWorker.instances[0].camera.ai_image_size)
        self.assertEqual("Cam A moi", self.catalog.get("a").name)

    def test_catalog_item_from_zone_service(self):
        item = {"id": "c1", "location_id": "l1", "name": "Cam", "source_type": "file", "source_url": "vtest.avi", "status": "active", "ai_image_size": 1280}
        cam = camera_from_catalog(item, INGEST)
        self.assertEqual(("c1", "FILE", 640, 360, 15, 1280), (cam.camera_id, cam.source_type, cam.frame_width, cam.frame_height, cam.target_fps, cam.ai_image_size))
        self.assertNotIn("inactive", RUNNING_STATUSES)


class VideoSourceTests(unittest.TestCase):
    def test_file_source_resolves_name_inside_video_dir(self):
        self.assertEqual(os.path.join("/videos", "vtest.avi"), resolve_video_path("vtest.avi", "/videos"))
        self.assertEqual("/data/a.mp4", resolve_video_path("file:///data/a.mp4", "/videos"))
        self.assertIsInstance(create_video_source("FILE", "vtest.avi", "/videos"), FileVideoSource)
        self.assertIsInstance(create_video_source("RTSP", "rtsp://x/y"), RtspVideoSource)

    def test_missing_file_does_not_open(self):
        self.assertFalse(FileVideoSource("/khong/co/file.mp4").open())

    def test_file_source_loops_at_end_and_keeps_pace(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "clip.avi")
            writer = cv2.VideoWriter(path, cv2.VideoWriter_fourcc(*"MJPG"), 10, (64, 48))
            for i in range(3):
                writer.write(np.full((48, 64, 3), i * 60, dtype=np.uint8))
            writer.release()

            now = [0.0]
            sleeps = []

            def sleep(seconds):
                sleeps.append(seconds)
                now[0] += seconds

            source = FileVideoSource(path, clock=lambda: now[0], sleep=sleep)
            self.assertTrue(source.open())
            frames = [source.read() for _ in range(7)]
            source.release()

            self.assertTrue(all(ok for ok, _ in frames))  # 3 khung hinh, doc 7 lan: da quay lai dau file
            self.assertAlmostEqual(0.6, sum(sleeps), places=5)  # khung 2..7 cach nhau 0.1 giay (10 FPS)


if __name__ == "__main__":
    unittest.main()
