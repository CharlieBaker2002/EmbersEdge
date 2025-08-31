import os
from PIL import Image

# Settings
INPUT_FOLDER = "/Users/charliebaker/Desktop/Imgs"       # Original images
OUTPUT_FOLDER = "/Users/charliebaker/Desktop/NEWIMGS"   # Cropped images + PDF
CROP_WIDTH = 1300
CROP_HEIGHT = 1800
LEFT = 1078
TOP = 325

def crop_image(image_path, save_path):
    img = Image.open(image_path)
    cropped = img.crop((LEFT, TOP, LEFT + CROP_WIDTH, TOP + CROP_HEIGHT))
    cropped.save(save_path)

def process_folder(input_folder, output_folder):
    os.makedirs(output_folder, exist_ok=True)
    png_files = sorted([f for f in os.listdir(input_folder) if f.lower().endswith(".png")])
    cropped_images = []

    for file in png_files:
        input_path = os.path.join(input_folder, file)
        output_path = os.path.join(output_folder, file)
        crop_image(input_path, output_path)
        cropped_images.append(Image.open(output_path).convert("RGB"))  # Needed for PDF

    if cropped_images:
        output_pdf_path = os.path.join(output_folder, "output.pdf")
        cropped_images[0].save(
            output_pdf_path,
            save_all=True,
            append_images=cropped_images[1:]
        )
        print(f"PDF saved to: {output_pdf_path}")
    else:
        print("No PNG files found in input folder.")

if __name__ == "__main__":
    process_folder(INPUT_FOLDER, OUTPUT_FOLDER)