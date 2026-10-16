"""Use the verified installed-Chrome hosted-page check."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).resolve().parents[2] / "deploy/cloudbase/tools/verify_hosted_login.py"),run_name="__main__")
