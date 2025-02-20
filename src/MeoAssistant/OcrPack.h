#pragma once
#include "AbstractResource.h"

#include <functional>

#include "AsstTypes.h"

namespace cv
{
    class Mat;
}
struct paddle_ocr_t;

namespace asst
{
    class OcrPack final : public SingletonHolder<OcrPack>, public AbstractResource
    {
        static constexpr size_t MaxBoxSize = 128;

    public:
        virtual ~OcrPack() override;

        virtual bool load(const std::filesystem::path& path) override;

        std::vector<TextRect> recognize(const cv::Mat& image, const TextRectProc& pred = nullptr,
                                        bool without_det = false);
        std::vector<TextRect> recognize(const cv::Mat& image, const Rect& roi, const TextRectProc& pred = nullptr,
                                        bool without_det = false);

    private:
        friend class SingletonHolder<OcrPack>;
        OcrPack();

        paddle_ocr_t* m_ocr = nullptr;

        // each box has 8 value ( 4 points, x and y )
        int m_boxes_buffer[MaxBoxSize * 8] = { 0 };
        char* m_strs_buffer[MaxBoxSize] = { nullptr };
        float m_scores_buffer[MaxBoxSize] = { 0 };
    };
}
