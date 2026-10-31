import os
import time
from typing import Optional, Tuple

import cv2
import numpy as np


class RtspVideoSource:
    def __init__(self, source_url: str) -> None:
        self.source_url = source_url
        self._capture: Optional[cv2.VideoCapture] = None

    def open(self) -> bool:
        self.release()
        self._capture = cv2.VideoCapture(self.source_url, cv2.CAP_FFMPEG)
        return self.is_opened()

    def is_opened(self) -> bool:
        return bool(self._capture is not None and self._capture.isOpened())

    def read(self) -> Tuple[bool, Optional[np.ndarray]]:
        if self._capture is None:
            return False, None
        ok, frame = self._capture.read()
        if not ok or frame is None:
            return False, None
        return True, frame

    def release(self) -> None:
        if self._capture is not None:
            self._capture.release()
            self._capture = None


class FileVideoSource(RtspVideoSource):
    """Phat lap mot file video nhu camera that (de an: "su dung video file de mo phong").

    Doc file nhanh hon thoi gian thuc rat nhieu, nen moi khung hinh duoc giu dung nhip FPS
    cua file; het file thi quay lai tu dau.
    """

    def __init__(self, path: str, clock=time.monotonic, sleep=time.sleep) -> None:
        super().__init__(path)
        self._clock = clock
        self._sleep = sleep
        self._frame_interval = 1.0 / 25
        self._next_frame_at = 0.0

    def open(self) -> bool:
        if not os.path.isfile(self.source_url):
            return False
        opened = super().open()
        if opened:
            fps = self._capture.get(cv2.CAP_PROP_FPS) or 0
            self._frame_interval = 1.0 / fps if 1 <= fps <= 120 else 1.0 / 25
            self._next_frame_at = self._clock()
        return opened

    def read(self) -> Tuple[bool, Optional[np.ndarray]]:
        ok, frame = super().read()
        if not ok and self._capture is not None:
            self._capture.set(cv2.CAP_PROP_POS_FRAMES, 0)
            ok, frame = super().read()
        if not ok:
            return False, None
        wait = self._next_frame_at - self._clock()
        if wait > 0:
            self._sleep(wait)
        # Tre qua mot giay (may ban) thi bat nhip lai, khong tua nhanh de duoi.
        self._next_frame_at = max(self._next_frame_at + self._frame_interval, self._clock() - 1.0)
        return True, frame


def resolve_video_path(source_url: str, video_dir: Optional[str]) -> str:
    """Camera loai FILE luu ten file (vd. vtest.avi); file nam trong thu muc video cua Edge Gateway."""
    path = source_url[len("file://"):] if source_url.startswith("file://") else source_url
    if os.path.isabs(path) or not video_dir:
        return path
    return os.path.join(video_dir, path)


def create_video_source(source_type: str, source_url: str, video_dir: Optional[str] = None):
    if source_type.upper() == "FILE":
        return FileVideoSource(resolve_video_path(source_url, video_dir or os.getenv("EDGE_VIDEO_DIR")))
    return RtspVideoSource(source_url)
